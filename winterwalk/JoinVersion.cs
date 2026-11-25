// What a friend is told when their copy does not match the host's.
//
// Before a join the puzzle SDK compares the two PCs' packs and, where they differ, covers the password screen with a
// message (its JoinPopup) that lists every puzzle that is missing or out of date. After a release that is nearly all
// of them: a screen-long list that is true but says nothing about which download to fetch. So:
//   - the host's lobby also carries the Big Christmas Walk version, as a lobby attribute ("bcwver") added right after
//     the SDK's own ("bpsdk");
//   - a joiner reads it, and the message leads with it: "You need Big Christmas Walk <version>", what this PC has,
//     and what to do. The list of puzzles is only shown where it is the one thing known: when both PCs say the same
//     version, or when the host's lobby does not say one (a host from before this was added, or other puzzles).
//
// A PC's version is the one its zip was packaged as: version.txt beside this plugin (tools\install\Package.ps1 writes
// it into the payload), else the installer's record (BepInEx\BigChristmasWalk.installed.txt). A copy with neither is a
// development build.
//
// Only a joiner that has this code sees the new message: a friend on an older release still gets the SDK's list.
//
// All of it hangs on the SDK mod's internals (LobbyData.Set / Get / ConvertPostfix, JoinPopup.Set / Open), found by
// name: with another SDK version they may be gone, and then the log says so and the SDK's own message is shown.
//
// For tests (the companion): get?path=WinterWalk.JoinVersion.State ; set?path=WinterWalk.JoinVersion.TestHost&value=1.2.3
// then get?path=WinterWalk.JoinVersion.Preview shows the message a joiner would get from such a host
// (TestHost "none": a host that says no version; "same": this PC's own).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WinterWalk
{
    internal static class JoinVersion
    {
        const string Key = "bcwver";                 // our lobby attribute
        const string SdkKey = "bpsdk";               // the SDK's main one: ours is added right after it
        const string SdkTitle = "PuzzleSDK versions don't match host:";      // JoinPopup's title, replaced by ours
        const string DevBuild = "a development build";
        const int ListShown = 8;                     // puzzle names shown before "and N more"

        public static string State = "not applied";
        public static string TestHost;               // for tests: see above
        public static string Last = "";              // the last message shown to this PC, title first

        static string mine;                          // this PC's version ("" = a development build)
        static string host;                          // the version the lobby last looked at says (null: it says none)
        static string heading = SdkTitle;
        static MethodInfo sdkSet, sdkGet;
        static bool adding, toldPublished, toldFailed;

        // This PC's version, "" for a development build
        internal static string Mine
        {
            get
            {
                if (mine != null) return mine;
                mine = "";
                try
                {
                    string beside = Path.Combine(Path.GetDirectoryName(typeof(JoinVersion).Assembly.Location) ?? "", "version.txt");
                    string record = Path.Combine(BepInEx.Paths.BepInExRootPath, "BigChristmasWalk.installed.txt");
                    if (File.Exists(beside)) mine = Clean(File.ReadAllText(beside));
                    else if (File.Exists(record))
                        foreach (string line in File.ReadLines(record))
                        {
                            if (line.StartsWith("# version ")) mine = Clean(line.Substring(10));
                            break;
                        }
                }
                catch (Exception) { mine = ""; }
                return mine;
            }
        }

        // What this PC calls itself, in the log and in the lobby
        internal static string Name => Mine.Length > 0 ? Mine : DevBuild;

        // A version as plain text that fits a lobby attribute and a line of the message
        static string Clean(string text)
        {
            if (text == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in text.Trim())
                if (c >= ' ' && c < 127 && c != '|' && c != '<' && c != '>' && sb.Length < 48) sb.Append(c);
            return sb.ToString();
        }

        internal static void Apply()
        {
            try
            {
                Type lobby = AccessTools.TypeByName("BigPuzzleSDK.LobbyData"), popup = AccessTools.TypeByName("BigPuzzleSDK.JoinPopup");
                sdkSet = lobby != null ? AccessTools.Method(lobby, "Set", new[] { typeof(object), typeof(object), typeof(string), typeof(string) }) : null;
                sdkGet = lobby != null ? AccessTools.Method(lobby, "Get", new[] { typeof(object), typeof(string) }) : null;
                MethodInfo read = lobby != null ? AccessTools.Method(lobby, "ConvertPostfix") : null;
                MethodInfo show = popup != null ? AccessTools.Method(popup, "Set", new[] { typeof(string[]) }) : null;
                MethodInfo open = popup != null ? AccessTools.Method(popup, "Open") : null;
                if (sdkSet == null || sdkGet == null || read == null || show == null)
                {
                    State = "not applied: the SDK's lobby code was not found (Set " + (sdkSet != null) + ", Get " + (sdkGet != null) + ", ConvertPostfix " + (read != null) + ", JoinPopup.Set " + (show != null) + ")";
                    Plugin.L.LogWarning("Winter Walk: " + State + ": a friend whose copy does not match is shown the SDK's own list");
                    return;
                }
                var harmony = new Harmony("bwm.winterwalk.join");
                harmony.Patch(sdkSet, postfix: new HarmonyMethod(typeof(JoinVersion), nameof(AfterSet)));
                harmony.Patch(read, prefix: new HarmonyMethod(typeof(JoinVersion), nameof(BeforeRead)));
                harmony.Patch(show, prefix: new HarmonyMethod(typeof(JoinVersion), nameof(BeforeShow)));
                bool titled = false;
                if (open != null)
                    try { harmony.Patch(open, transpiler: new HarmonyMethod(typeof(JoinVersion), nameof(Retitle))); titled = retitled; }
                    catch (Exception e) { Plugin.L.LogWarning("Winter Walk: the join message keeps the SDK's title: " + e.GetType().Name + ": " + e.Message); }
                State = "applied, this PC is " + Name + (titled ? "" : " (the SDK's title is kept)");
                Plugin.L.LogInfo("Winter Walk: Big Christmas Walk " + Name + "; a lobby hosted here says so, and a friend whose copy differs is told which version to get" + (titled ? "" : " (under the SDK's own title)"));
            }
            catch (Exception e)
            {
                State = "not applied: " + e.GetType().Name + ": " + e.Message;
                Plugin.L.LogWarning("Winter Walk: the join message was not changed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ---- host: the lobby says which version it is ----
        // LobbyData.Set(object manager, object modification, string key, string value) adds one attribute to a lobby
        // change; after the SDK's main one, ours goes into the same change
        static void AfterSet(object manager, object modification, string key)
        {
            if (adding || key != SdkKey) return;
            adding = true;
            try
            {
                sdkSet.Invoke(null, new object[] { manager, modification, Key, Name });
                if (!toldPublished) { toldPublished = true; Plugin.L.LogInfo("Winter Walk: the lobby says Big Christmas Walk " + Name); }
            }
            catch (Exception e)
            {
                if (!toldFailed) { toldFailed = true; Exception inner = e.InnerException ?? e; Plugin.L.LogWarning("Winter Walk: the lobby could not be told the version: " + inner.GetType().Name + ": " + inner.Message); }
            }
            finally { adding = false; }
        }

        // ---- anyone: what the lobby just found says ----
        // LobbyData.ConvertPostfix(object[] __args) is the SDK's own look at a lobby (its args are the game's: the first
        // is the lobby's details); it works out what does not match and calls JoinPopup.Set. Ours is read just before.
        static void BeforeRead(object[] __0)
        {
            host = null;
            try
            {
                if (__0 == null || __0.Length < 1 || __0[0] == null) return;
                string said = sdkGet.Invoke(null, new object[] { __0[0], Key }) as string;
                if (!string.IsNullOrEmpty(said)) host = Clean(said);
            }
            catch (Exception) { host = null; }
        }

        // ---- a joiner whose copy does not match: the message ----
        // JoinPopup.Set(string[] lines): the SDK's lines (each may hold several, split by '\n') become ours
        static void BeforeShow(ref string[] lines)
        {
            if (lines == null || lines.Length == 0) return;
            try
            {
                string[] theirs = lines;
                lines = Compose(theirs, host, out heading);
                Last = heading + " | " + string.Join(" | ", lines).Replace('\n', ' ');
                Plugin.L.LogInfo("Winter Walk: join refused, the host has " + (host ?? "an unnamed version") + " and this PC " + Name + ". The SDK found: " + string.Join(" | ", theirs).Replace('\n', ' '));
            }
            catch (Exception e) { heading = SdkTitle; Plugin.L.LogWarning("Winter Walk: the join message is the SDK's own: " + e.GetType().Name + ": " + e.Message); }
        }

        static string[] Compose(string[] theirs, string hostSays, out string title)
        {
            string have = Mine.Length > 0 ? "version " + Mine : DevBuild;
            var o = new List<string>();
            if (string.IsNullOrEmpty(hostSays))
            {
                // a host from before the lobby said its version, or a lobby of somebody else's puzzles
                title = "This PC does not match the host";
                o.Add("The host's lobby does not say which Big Christmas Walk it runs: an older one than yours, or other puzzles.");
                o.Add("You have " + have + ".");
                o.Add("Ask the host which version they have, and install the same.");
                o.AddRange(Short(theirs));
            }
            else if (hostSays == Name)
            {
                title = "Same version, but some files differ";
                o.Add("Both PCs have " + (hostSays == DevBuild ? DevBuild + " of Big Christmas Walk" : "Big Christmas Walk " + hostSays) + ", yet these do not match:");
                o.AddRange(Short(theirs));
                o.Add("Run Install.cmd from the zip again, on this PC and on the host's.");
            }
            else
            {
                title = "You need Big Christmas Walk " + hostSays;
                o.Add("The host has " + (hostSays == DevBuild ? hostSays : "version " + hostSays) + ".");
                o.Add("You have " + have + ".");
                o.Add("To join, install the host's version: ask them for the same zip and run its Install.cmd.");
            }
            return o.ToArray();
        }

        // The SDK's lists, cut to a few names each (after a release nearly every puzzle is on them)
        static IEnumerable<string> Short(string[] theirs)
        {
            foreach (string block in theirs)
            {
                if (block == null) continue;
                string[] rows = block.Split('\n');
                if (rows.Length <= ListShown + 2) { yield return block; continue; }
                yield return string.Join("\n", rows, 0, ListShown + 1) + "\nand " + (rows.Length - ListShown - 1) + " more";
            }
        }

        // JoinPopup.Open builds its text from a literal that holds its title (the compiler folds the title's markup and
        // the title into one): that literal becomes a call for ours, with whatever stood round the title kept
        static bool retitled;
        static string titleBefore = "", titleAfter = "";
        static IEnumerable<CodeInstruction> Retitle(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo ours = AccessTools.Method(typeof(JoinVersion), nameof(TitleText));
            foreach (CodeInstruction instruction in instructions)
            {
                if (!retitled && instruction.opcode == OpCodes.Ldstr && instruction.operand is string text && text.Contains(SdkTitle))
                {
                    int at = text.IndexOf(SdkTitle, StringComparison.Ordinal);
                    titleBefore = text.Substring(0, at);
                    titleAfter = text.Substring(at + SdkTitle.Length);
                    retitled = true;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = ours;
                }
                yield return instruction;
            }
        }

        static string TitleText() => titleBefore + heading + titleAfter;

        // For tests: the message a joiner would see from a host that says TestHost
        public static string Preview
        {
            get
            {
                string says = TestHost == "none" || string.IsNullOrEmpty(TestHost) ? null : TestHost == "same" ? Name : Clean(TestHost);
                string[] sample = { "Out of date:\nAdvent Trail\nBauble Hunt\nBell Ringers\nBig Air\nBlind Carols\nBlind Sculptor\nBlizzard Walk\nDownhill\nFairy Lights\nFootprints\nHold the Star\nLight the Tree" };
                string[] lines = Compose(sample, says, out string title);
                return title + "\n" + string.Join("\n", lines);
            }
        }
    }
}
