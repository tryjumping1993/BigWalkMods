// Winter Walk: night-vision goggles that see in the dark.
//
// The game's night-vision goggles are an unfinished item: they sit switched off in its hidden "SecretZone", and the
// call they make when a player looks through them (PostProcessingManager.SetNightVision) changes nothing, here or on
// the mainland (2026-11-24: none of the game's post-processing volumes moves, and a screenshot at midnight is the same
// with it on or off; its x-ray goggles' call does raise their volume). The puzzle SDK hands them out all the same, and
// the island's gear stations have them (players: "night vision goggles don't actually do anything").
//
// So this makes them work, for the player wearing them only: while any goggles marked as giving night vision are in
// use by this PC's player (PeckEffectTelescope.isActiveForLocalPlayer), a green light with no shadows shines the way
// they look, tilted down so the ground ahead takes it too, the same strength at any distance: everything they look at
// shows in green as through an image intensifier. Our own shaders take it as they take any lamp, and the game's as
// any light. Nobody else sees it: the light exists on the wearer's PC alone.
// (The game's own switch is no use as the signal: it is thrown about 90 times a frame, by every telescope and pair of
// goggles in the world, and hooking it gave "on" with nothing worn.)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterWalk
{
    public class NightVision : MonoBehaviour
    {
        public NightVision(IntPtr ptr) : base(ptr) { }

        public static bool TestOn;                 // live, for tests: as if goggles were on
        public static float Strength = 0.6f;       // the light's intensity
        public static string State = "not started";
        public static float Tilt = 32f;            // degrees the light points below the wearer's view
        public static bool On;
        public static string Where = "";           // where the world's night-vision goggles are, for tests

        Light lamp;
        float level, nextLook;
        readonly List<PeckEffectTelescope> goggles = new List<PeckEffectTelescope>();

        void LateUpdate()
        {
            using var diag = Diag.Time("night vision");
            try
            {
                float now = Time.unscaledTime;
                bool worn = false;
                if (now >= nextLook)
                {
                    // the goggles there are (the SDK's copies come with the puzzles), looked for now and then
                    nextLook = now + 5f;
                    goggles.Clear();
                    foreach (PeckEffectTelescope scope in FindObjectsOfType<PeckEffectTelescope>())
                        if (scope != null && scope.givesNightVision) goggles.Add(scope);
                    var where = new System.Text.StringBuilder();
                    foreach (PeckEffectTelescope scope in goggles) { Vector3 at = scope.transform.position; where.Append(at.x.ToString("0.0")).Append(", ").Append(at.y.ToString("0.0")).Append(", ").Append(at.z.ToString("0.0")).Append("; "); }
                    Where = where.ToString();
                }
                for (int i = 0; i < goggles.Count; i++)
                {
                    PeckEffectTelescope scope = goggles[i];
                    if (scope == null) { nextLook = 0f; continue; }
                    if (scope.isActiveForLocalPlayer) { worn = true; break; }
                }
                On = TestOn || worn;
                float want = On ? 1f : 0f;
                if (level == want && !On) { State = "off (" + goggles.Count + " night-vision goggles in the world)"; return; }
                level = Mathf.MoveTowards(level, want, Time.unscaledDeltaTime * 4f);
                Camera cam = Camera.main;
                if (cam == null) return;
                if (lamp == null)
                {
                    var go = new GameObject("Winter Walk Night Vision");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    lamp = go.AddComponent<Light>();
                    lamp.type = LightType.Directional;
                    lamp.shadows = LightShadows.None;
                    lamp.color = new Color(0.3f, 1f, 0.38f, 1f);
                }
                // along the wearer's view, tilted down: flat ground ahead is square to a level beam and took none of it
                Vector3 view = cam.transform.forward;
                float yaw = Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(Mathf.Clamp(view.y, -1f, 1f)) * Mathf.Rad2Deg;
                lamp.transform.rotation = Quaternion.Euler(Mathf.Clamp(pitch + Tilt, 20f, 85f), yaw, 0f);
                lamp.intensity = Strength * level;
                bool lit = level > 0.01f;
                if (lamp.enabled != lit) lamp.enabled = lit;
                State = (On ? "on" : "fading out") + (worn ? " (goggles worn)" : TestOn ? " (test)" : "");
            }
            catch (Exception e)
            {
                if (!State.StartsWith("stopped")) Plugin.L.LogWarning("Winter Walk: night vision: " + e.GetType().Name + ": " + e.Message);
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                nextLook = Time.unscaledTime + 10f;
            }
        }
    }
}
