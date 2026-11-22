// Big Air, "Judges' Call": a Big Air final on the right-hand run (run 2). Three big kickers stand side by side, Red
// on the left looking downhill, Green in the middle, Gold on the right, and each throws a rider into its own window:
// a counted box 6 m over the landing, framed in flags under a finish gantry.
//
// A judge is shut in a windowless, soundproof cabin on a pad beside the course. Each round one of three coloured
// lamps lights in there, and the riders must fly the matching window. The judge's only link out is a one-way
// intercom to the start hut 90 m up the run, where the riders gear up and where the clock and the start switch are.
// A wrong window fails the run; 4 / 6 / 8 clean calls (2 / 3 / 4 players) inside the 5-minute clock win the gourd, so
// riders take turns from the hut and must never both fly the same call.
//
// Parts only (Press In Order, Presence Sensors, Intercom, soundproof Door, No Items Gate, Radio Dead Zone, Timer,
// Progress Bars, PuzzleHider); no custom script. Jetpacks do not fire over the course (the "Calm Air Big Air" box,
// read by Winter Walk). The hut's racks and the removal of two terrain-park kickers from the in-run are the island's
// (WinterIslePuzzle step 11c), not this pack's.
//
// Frames: the course is in run coordinates, C(along, u) = RunCentre(2, along) + right * u, u to the rider's right
// looking downhill (the pad's side). The pad is turned by padYaw (the run's heading at along 1112): x' points away
// from the run, z' downhill. The root keeps yaw 0.
using System;
using System.Collections.Generic;
using Bwm;
using UnityEngine;

public sealed class BigAirPuzzle : PuzzleDef
{
    public override string Name => "BigAir";
    public override Vector2 Footprint => new Vector2(18f, 18f);      // the 14 x 12 pad turned about 30 degrees
    public const int Run = 2;

    // ---- the site: the pad beside the terrain park, its levelling stopping short of the run ----
    const float PadAlong = 1112f, PadOut = 30f;
    public override Vector2? FixedSite
    {
        get
        {
            Vector2 mid = RC(PadAlong, out float half);
            return mid + Right(Down(PadAlong)) * (half + PadOut);
        }
    }

    // ---- the kickers and the flight model (heights in metres, speeds in m/s) ----
    const float KickerFoot = 1118f, KickerLen = 10f, KickerWidth = 4f, KickerBase = -0.04f;
    const int KickerSteps = 12;
    const float LaunchDeg = 22f;          // world angle of the lip's last face, above horizontal (a lane steepens it if it must)
    const float MaxLaunchDeg = 30f;       // the steepest a lane may go before the build fails
    const float DesignSpeed = 10f;        // the slowest committed rider expected at the lip
    const float Body = 0.75f;             // a player's origin over their feet
    const float Probe = 1.0f;             // the presence sensor's highest test point over the origin
    const float Gravity = 9.81f;
    // ---- the windows ----
    const float WinAcross = 4f, WinTall = 3.5f, WinAlong = 4f, WinOverGround = 6f, MinWinX = 6f, MaxWinX = 14f;
    // on foot from a lip (generous: the game's sprint is 3.5 m/s): a sprint-jump, and a jump off a partner's head
    const float WalkSpeed = 4.5f, WalkApex = 1.3f, StackSpeed = 3f, StackApex = 1f, StackHeight = 1.7f;
    const float StackClear = 5.2f;        // a window's bottom over the highest ground under it
    const float ReachMargin = 0.5f;       // a (DesignSpeed - 1) rider's probe over a window's bottom
    // ---- the rest ----
    const float HutAlong = 1030f;
    const float Floor = 0.3f;             // the cabin's floor over the root's level
    const float ClockSeconds = 300f;
    const float CalmAlong = 1095f, CalmHalfAlong = 105f, CalmHalfAcross = 45f, CalmUp = 150f, CalmUnder = 10f;
    // the cabin's door (in the +x' wall) and the partition's opening (z')
    const float DoorZ0 = -1.82f, DoorZ1 = -0.58f, DoorH = 2.04f, Cover = 0.06f;
    const float GateZ0 = 0.28f, GateZ1 = 2.40f;

    static readonly string[] LaneNames = { "Red", "Green", "Gold" };
    static readonly float[] LaneU = { -5f, 0f, 5f };
    static readonly Color[] LaneColours = { IsleKit.Red, IsleKit.Green, IsleKit.Gold };
    static readonly Color[] LampColours = { new Color(0.85f, 0.12f, 0.1f), new Color(0.15f, 0.6f, 0.25f), new Color(0.95f, 0.75f, 0.2f) };
    static readonly Color Snow = new Color(0.94f, 0.96f, 1f);
    static readonly Color Steel = new Color(0.32f, 0.33f, 0.36f);
    static readonly Color Cord = new Color(0.12f, 0.12f, 0.13f);

    static Vector2 RC(float along, out float half) => WinterIslePuzzle.RunCentre(Run, along, out half);
    static Vector2 RC(float along) => WinterIslePuzzle.RunCentre(Run, along, out _);
    static Vector2 Down(float along) => (RC(along + 1f) - RC(along - 1f)).normalized;
    static Vector2 Right(Vector2 down) => new Vector2(down.y, -down.x);
    static float YawOf(Vector2 v) => Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;

    PuzzleBuilder b;
    Vector3 site;
    float padYaw;

    // ground height (world) from the island's land grid; the build stops if there is none
    float G(Vector2 w)
    {
        if (!b.Ground.At(w.x, w.y, out float y)) throw new Exception("BigAir: no ground at (" + Sdk.F(w.x) + ", " + Sdk.F(w.y) + ")");
        return y;
    }
    // a world point as the root's local space (mesh vertices, light posts), 'lift' over the ground, or at an absolute height
    Vector3 OnGround(Vector2 w, float lift) => new Vector3(w.x - site.x, G(w) + lift - site.y, w.y - site.z);
    Vector3 AtAbs(Vector2 w, float y) => new Vector3(w.x - site.x, y - site.y, w.y - site.z);
    Piece PartAbs(string prefab, Vector2 w, float y, float yaw) => b.Part(prefab, w.x - site.x, w.y - site.z, y - site.y, yaw, false);
    Piece PartOnGround(string prefab, Vector2 w, float h, float yaw) => b.Part(prefab, w.x - site.x, w.y - site.z, h, yaw, true);
    // the pad's frame: local offsets, parts at the cabin's floor
    Vector2 Pad(float x, float z) => IsleKit.Turn(x, z, padYaw);
    Piece PadPart(string prefab, float x, float z, float h, float yawOff)
    {
        Vector2 p = Pad(x, z);
        return b.Part(prefab, p.x, p.y, Floor + h, padYaw + yawOff, false);
    }
    // a block in the pad's frame from (x0, z0) to (x1, z1), heights over the root's level
    void PadBox(MeshBuilder mb, float x0, float x1, float z0, float z1, float y0, float y1, Color c)
    {
        Vector2 mid = Pad((x0 + x1) / 2f, (z0 + z1) / 2f);
        Kit.AddBox(mb, new Vector3(mid.x, y0, mid.y), new Vector3(x1 - x0, y1 - y0, z1 - z0), c, padYaw);
    }

    public override void Build(PuzzleBuilder builder)
    {
        b = builder;
        site = b.Root.transform.position;
        padYaw = YawOf(Down(PadAlong));
        Material snowy = IsleKit.Snowy(b), bare = IsleKit.Bare(b);
        b.ClearGround(18f, 18f);
        Vector2 tp = Pad(4f, -2f);
        b.DebugTeleport(tp.x, tp.y);

        // ================= the course: three kickers, their windows, the dye lines =================
        Vector2 kFoot0 = RC(KickerFoot);
        Vector2 kDown = (RC(KickerFoot + KickerLen) - kFoot0).normalized, kRight = Right(kDown);
        float laneYaw = YawOf(kDown);
        var kickers = new MeshBuilder();
        var flags = new MeshBuilder();
        var gateComps = new Component[3];
        var boxCentres = new Vector2[3];
        var bottoms = new float[3];
        var failures = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            Vector2 foot = kFoot0 + kRight * LaneU[i], lip = foot + kDown * KickerLen;
            float slope = Mathf.Atan2(G(lip - kDown * 3f) - G(lip + kDown * 3f), 6f);      // the ground's fall at the lip (radians)
            // The launch starts at LaunchDeg and steepens by half degrees (to MaxLaunchDeg) until the lane passes its
            // checks: on gentler ground the same launch flies lower over the snow (the run here falls 11-15 degrees).
            float lipH = 0f, bottom = 0f;
            Vector2 bc = lip;
            string report = "";
            bool ok = false;
            for (float deg = LaunchDeg; deg <= MaxLaunchDeg + 0.01f && !ok; deg += 0.5f)
            {
                float tanLaunch = Mathf.Tan(deg * Mathf.Deg2Rad);
                // profile h = lipH * t^2 over the ground: the last face's chord rises (2 - 1/N) lipH / L over the ground,
                // so on a plane of this slope its world angle is the launch angle
                float lh = KickerLen * (tanLaunch + Mathf.Tan(slope)) / (2f - 1f / KickerSteps);
                float H(float t) => lh * t * t + KickerBase;
                // the launch as built: SkiDriver keeps the rider's velocity in the last face's plane
                float lipAbs = G(lip) + H(1f);
                float prevAbs = G(Vector2.Lerp(foot, lip, (KickerSteps - 1f) / KickerSteps)) + H((KickerSteps - 1f) / KickerSteps);
                float beta = Mathf.Atan2(lipAbs - prevAbs, KickerLen / KickerSteps);
                float tanBeta = Mathf.Tan(beta);
                // the rider's origin (absolute height) x metres past the lip, horizontally, at speed v off the lip
                float Origin(float v, float x)
                {
                    float vx = v * Mathf.Cos(beta);
                    return lipAbs + Body + tanBeta * x - Gravity * x * x / (2f * vx * vx);
                }
                float PeakOverGround(float v)
                {
                    float best = float.MinValue;
                    for (float x = 0f; x <= 20f; x += 0.25f) best = Mathf.Max(best, Origin(v, x) - Body - G(lip + kDown * x));
                    return best;
                }
                // a window centred x0 past the lip: its bottom is 6 m over the ground at its centre, raised where needed
                // so that it stays StackClear over the highest ground under it (the ground under the landing is uneven)
                float Highest(float x0)
                {
                    float hi = float.MinValue;
                    Vector2 c = lip + kDown * x0;
                    for (float a = -WinAcross / 2f; a <= WinAcross / 2f + 0.01f; a += 0.5f)
                        for (float z = -WinAlong / 2f; z <= WinAlong / 2f + 0.01f; z += 0.5f)
                            hi = Mathf.Max(hi, G(c + kRight * a + kDown * z));
                    return hi;
                }
                float BottomAt(float x0) => Mathf.Max(G(lip + kDown * x0) + WinOverGround, Highest(x0) + StackClear);
                float vxD = DesignSpeed * Mathf.Cos(beta);
                float xStar = (tanBeta + Mathf.Tan(slope)) * vxD * vxD / Gravity;      // the peak over the ground, on a plane
                // on foot, with no gear: a sprint-jump off the lip, or a jump off a partner's head standing on it. Its
                // highest test point over the box's length, over the box's bottom, with the box centred x0 past the lip
                float OnFootOver(float x0)
                {
                    float worst = float.MinValue, bottomAt = BottomAt(x0);
                    foreach (var (speed, apex, start) in new[] { (WalkSpeed, WalkApex, 0f), (StackSpeed, StackApex, StackHeight) })
                    {
                        float vy = Mathf.Sqrt(2f * Gravity * apex);
                        for (float x = x0 - WinAlong / 2f; x <= x0 + WinAlong / 2f + 0.01f; x += 0.1f)
                        {
                            float t = x / speed;
                            worst = Mathf.Max(worst, lipAbs + Body + start + vy * t - Gravity * t * t / 2f + Probe - bottomAt);
                        }
                    }
                    return worst;
                }
                // the window's centre: at the design rider's peak, at least MinWinX out, and far enough that nobody on
                // foot reaches it from the lip (the reach check below then decides whether a rider still can)
                float xc = Mathf.Max(xStar, MinWinX);
                while (xc < MaxWinX && OnFootOver(xc) > -ReachMargin) xc += 0.25f;
                float onFoot = OnFootOver(xc);
                Vector2 c0 = lip + kDown * xc;
                float bot = BottomAt(xc);
                float clear = bot - Highest(xc);
                float reach = float.MinValue;      // the slower rider's highest test point over the bottom, within the box's length
                for (float x = xc - WinAlong / 2f; x <= xc + WinAlong / 2f + 0.01f; x += 0.1f)
                    reach = Mathf.Max(reach, Origin(DesignSpeed - 1f, x) + Probe - bot);
                report = LaneNames[i] + " lane: ground slope " + Sdk.F(slope * Mathf.Rad2Deg) + " deg, lip " + Sdk.F(lh)
                    + " m, launch " + Sdk.F(beta * Mathf.Rad2Deg) + " deg (world, designed " + Sdk.F(deg) + "), peak at " + Sdk.F(xStar)
                    + " m past the lip, window centre " + Sdk.F(xc) + " m past it, bottom " + Sdk.F(bot - G(c0)) + " m over the ground at its centre, "
                    + Sdk.F(clear) + " m over the highest ground under it (need " + Sdk.F(StackClear)
                    + "), a " + Sdk.F(DesignSpeed - 1f) + " m/s rider's probe " + Sdk.F(reach) + " m over the bottom (need " + Sdk.F(ReachMargin)
                    + "), on foot from the lip the probe comes " + Sdk.F(onFoot) + " m over the bottom (need under " + Sdk.F(-ReachMargin)
                    + "), peak over the ground " + Sdk.F(PeakOverGround(DesignSpeed)) + " m at " + Sdk.F(DesignSpeed) + " m/s, "
                    + Sdk.F(PeakOverGround(DesignSpeed + 2f)) + " m at " + Sdk.F(DesignSpeed + 2f) + " m/s";
                ok = clear >= StackClear - 0.001f && reach >= ReachMargin && onFoot <= -ReachMargin;
                lipH = lh; bc = c0; bottom = bot;
            }
            Debug.Log("[BWM] Big Air " + report);
            if (!ok) failures.Add(report);

            // the kicker as chosen
            float HOf(float t) => lipH * t * t + KickerBase;
            Vector2 hw = kRight * (KickerWidth / 2f);
            for (int st = 0; st < KickerSteps; st++)
            {
                float t0 = (float)st / KickerSteps, t1 = (float)(st + 1) / KickerSteps;
                Vector2 a0 = Vector2.Lerp(foot, lip, t0), a1 = Vector2.Lerp(foot, lip, t1);
                Vector3 aL = OnGround(a0 - hw, HOf(t0)), aR = OnGround(a0 + hw, HOf(t0)), cL = OnGround(a1 - hw, HOf(t1)), cR = OnGround(a1 + hw, HOf(t1));
                kickers.Quad(aL, cL, cR, aR, Snow); kickers.Quad(aL, aR, cR, cL, Snow);      // the riding surface (both ways)
                kickers.Quad(OnGround(a0 - hw, -0.3f), OnGround(a1 - hw, -0.3f), cL, aL, Snow);
                kickers.Quad(OnGround(a0 + hw, -0.3f), aR, cR, OnGround(a1 + hw, -0.3f), Snow);
                if (st == KickerSteps - 1) kickers.Quad(OnGround(a1 - hw, -0.3f), cL, cR, OnGround(a1 + hw, -0.3f), Snow);      // the lip's face
            }

            // the counted window: a player-presence box, its drawn frame added with the gantry below
            Piece gate = PartAbs("Presence Sensor", bc, bottom + WinTall / 2f, laneYaw).Named("Gate " + LaneNames[i]);
            BoxCollider box = gate.Go.GetComponent<BoxCollider>();
            if (box == null) box = gate.Go.AddComponent<BoxCollider>();
            box.size = new Vector3(WinAcross, WinTall, WinAlong);
            box.center = Vector3.zero;
            gate.Set("PuzzlePresenceSensor", "type", 0).Set("PuzzlePresenceSensor", "requiredPlayers", 1);
            gateComps[i] = gate.Component("PuzzlePresenceSensor");
            boxCentres[i] = bc;
            bottoms[i] = bottom;

            // a flag pole of the lane's colour at both corners of the lip (no collider: nothing to snag a rider)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector2 f = lip + kRight * (sgn * (KickerWidth / 2f + 0.25f));
                Kit.AddBox(flags, OnGround(f, -0.1f), new Vector3(0.08f, 2.5f, 0.08f), Steel);
                Kit.AddBox(flags, OnGround(f, 1.8f) + new Vector3(kDown.x, 0f, kDown.y) * 0.35f, new Vector3(0.04f, 0.5f, 0.7f), LaneColours[i], laneYaw);
            }
        }
        if (failures.Count > 0)
            throw new Exception("BigAir: a window is unreachable or can be reached from the ground (move the kicker foot up the run or lengthen the in-run before lowering a window; DesignSpeed is "
                + Sdk.F(DesignSpeed) + " m/s): " + string.Join(" | ", failures));
        // (the object is named as the pistes' sheet, which Winter Walk rides; the mesh keeps its own name)
        b.MeshPiece("Big Air Kickers", kickers.ToMesh(), snowy, true).Named("Piste Sheet");
        b.MeshPiece("Big Air Kicker Flags", flags.ToMesh(), bare, false);

        // dye lines down the in-run to each kicker's foot: strips of the lane's colour, no collider
        var dye = new MeshBuilder();
        for (int i = 0; i < 3; i++)
        {
            var line = new List<Vector2>();
            for (float a = 1040f; a < KickerFoot - 1f; a += 2f) line.Add(RC(a) + Right(Down(a)) * LaneU[i]);
            line.Add(kFoot0 + kRight * LaneU[i]);
            for (int k = 0; k + 1 < line.Count; k++)
            {
                Vector2 p0 = line[k], p1 = line[k + 1];
                Vector2 across = Right((p1 - p0).normalized) * 0.175f;
                Vector3 a0 = OnGround(p0 - across, 0.1f), b0 = OnGround(p0 + across, 0.1f), a1 = OnGround(p1 - across, 0.1f), b1 = OnGround(p1 + across, 0.1f);
                dye.Quad(a0, a1, b1, b0, LaneColours[i]); dye.Quad(a0, b0, b1, a1, LaneColours[i]);
            }
        }
        b.MeshPiece("Big Air Dye Lines", dye.ToMesh(), bare, false);

        // ================= the finish gantry over the windows =================
        Vector2 gMid = (boxCentres[0] + boxCentres[1] + boxCentres[2]) / 3f;
        float gAlong = (gMid - WinterIslePuzzle.Peak).magnitude;
        Vector2 runAtGantry = RC(gAlong, out float gHalf);
        float runOff = Vector2.Dot(runAtGantry - gMid, kRight);      // the run's middle from the gantry's, across
        float mastU = Mathf.Max(13f, gHalf + 1.5f);
        Vector2 mastL = gMid + kRight * (runOff - mastU), mastR = gMid + kRight * (runOff + mastU);
        float maxTop = Mathf.Max(bottoms[0], Mathf.Max(bottoms[1], bottoms[2])) + WinTall;
        float bannerBottom = maxTop + 1f, trussBottom = bannerBottom + 1f, trussTop = trussBottom + 0.5f;
        var masts = new MeshBuilder();
        foreach (Vector2 m in new[] { mastL, mastR })
        {
            float foot = G(m) - 0.3f;
            Kit.AddBox(masts, AtAbs(m, foot), new Vector3(0.35f, trussTop - foot, 0.35f), IsleKit.DarkWood, laneYaw);
            Kit.AddBox(masts, AtAbs(m, foot), new Vector3(0.9f, 0.6f, 0.9f), IsleKit.Stone, laneYaw);      // its footing
        }
        b.MeshPiece("Big Air Masts", masts.ToMesh(), snowy, true);
        // truss, banners and window frames: no collider, so nothing over the windows can be stood on
        var truss = new MeshBuilder();
        Kit.AddBox(truss, AtAbs((mastL + mastR) / 2f, trussBottom), new Vector3((mastR - mastL).magnitude + 0.35f, 0.5f, 0.5f), Steel, laneYaw);
        for (int i = 0; i < 3; i++)
        {
            Vector2 bc = boxCentres[i];
            Vector2 onTruss = gMid + kRight * Vector2.Dot(bc - gMid, kRight);
            Kit.AddBox(truss, AtAbs(onTruss, bannerBottom), new Vector3(3.6f, 1f, 0.08f), LaneColours[i], laneYaw);
            // the drawn frame at the box's middle: sides at its sides, from 1 m under its bottom to 0.3 m under its top
            float low = bottoms[i] - 1f, high = bottoms[i] + WinTall - 0.3f;
            Vector2 left = bc - kRight * (WinAcross / 2f), right = bc + kRight * (WinAcross / 2f);
            int n = 0;
            foreach (Vector2 side in new[] { left, right })
            {
                for (float y = low; y < high - 0.05f; y += 0.3f, n++)
                    Kit.AddBox(truss, AtAbs(side, y), new Vector3(0.12f, Mathf.Min(0.28f, high - y), 0.12f), n % 2 == 0 ? LaneColours[i] : IsleKit.Cream, laneYaw);
                Kit.AddBox(truss, AtAbs(side, high), new Vector3(0.03f, bannerBottom - high, 0.03f), Cord, laneYaw);      // its cord up to the gantry
            }
            const int Pennants = 13;
            foreach (float y in new[] { low, high })
                for (int k = 0; k < Pennants; k++)
                    Kit.AddBox(truss, AtAbs(Vector2.Lerp(left, right, (k + 0.5f) / Pennants), y - 0.06f), new Vector3(WinAcross / Pennants - 0.02f, 0.12f, 0.12f),
                        k % 2 == 0 ? LaneColours[i] : IsleKit.Cream, laneYaw);
        }
        b.MeshPiece("Big Air Truss", truss.ToMesh(), bare, false);
        Vector2 uphill = -kDown * 0.3f;
        Vector3 lightL = AtAbs(mastL + uphill, trussBottom - 0.05f), lightR = AtAbs(mastR + uphill, trussBottom - 0.05f);
        Kit.LightString(b, "Gantry Lights", new List<Vector3> { lightL, lightR }, 0.02f, 0.5f, false, false, 41);

        // the Gantry Bar on its own board outside the piste, beyond the +u mast, facing uphill
        Vector2 boardAt = gMid + kRight * (runOff + mastU + 3f);
        float boardYaw = laneYaw + 180f;
        IsleKit.BoardOnPosts(b, "Gantry Board", boardAt.x - site.x, boardAt.y - site.z, boardYaw, 4.4f, 2.4f, 3.6f, IsleKit.Board);
        Piece gantryBar = PartOnGround("Progress Bar", boardAt - kDown * (IsleKit.FaceZ + 0.03f), 3.0f, boardYaw).Named("Gantry Bar").Scaled(1.15f, 1.15f, 1.15f);
        gantryBar.Set("PuzzleProgressBar", "total", 6);
        PartOnGround("Confetti", gMid + kRight * (runOff - mastU + 0.8f) + kDown * 2f, 0.2f, 0f).Named("Gantry Confetti L");
        PartOnGround("Confetti", gMid + kRight * (runOff + mastU - 0.8f) + kDown * 2f, 0.2f, 0f).Named("Gantry Confetti R");

        // ================= the start hut up the run: speaker, clock, bars, switch =================
        Vector2 hO = RC(HutAlong, out float hHalf);
        Vector2 hD = Down(HutAlong), hS = Right(hD);
        float hutYaw = YawOf(-hS);      // facing the run (its local x runs down the run)
        Vector2 HW(float da, float u) => hO + hD * da + hS * (hHalf + u);      // u from the run's edge
        float deck = float.MinValue;
        foreach (float da in new[] { -2f, 0f, 2f })
            foreach (float u in new[] { 3.5f, 5f, 6.6f })
                deck = Mathf.Max(deck, G(HW(da, u)));
        deck += 0.25f;
        var hut = new MeshBuilder();
        Kit.AddBox(hut, AtAbs(HW(0f, 5.05f), deck - 0.2f), new Vector3(4f, 0.2f, 3.1f), IsleKit.Wood, hutYaw);           // the deck
        Kit.AddBox(hut, AtAbs(HW(0f, 6.5f), deck - 0.05f), new Vector3(4f, 3.05f, 0.15f), IsleKit.Wood, hutYaw);         // the back wall
        foreach (float da in new[] { -1.92f, 1.92f })
            Kit.AddBox(hut, AtAbs(HW(da, 3.58f), deck), new Vector3(0.15f, 3f, 0.15f), IsleKit.DarkWood, hutYaw);         // the front posts
        Kit.AddBox(hut, AtAbs(HW(0f, 5f), deck + 3f), new Vector3(4.6f, 0.15f, 3.9f), IsleKit.Red, hutYaw);               // the roof
        foreach (float da in new[] { -1.85f, 0f, 1.85f })      // stilts down to the snow
            foreach (float u in new[] { 3.6f, 6.5f })
            {
                Vector2 p = HW(da, u);
                float foot = G(p) - 0.3f, height = deck - 0.2f - foot;
                if (height > 0.05f) Kit.AddBox(hut, AtAbs(p, foot), new Vector3(0.18f, height, 0.18f), IsleKit.DarkWood, hutYaw);
            }
        {      // steps down to the snow on the run's side
            float drop = deck - G(HW(0f, 3.5f));
            int steps = Mathf.Clamp(Mathf.CeilToInt(drop / 0.3f) - 1, 0, 6);
            float rise = drop / (steps + 1);
            float stepFoot = float.MaxValue;
            foreach (float da in new[] { -2f, 0f, 2f })
                foreach (float u in new[] { 3.5f, 3.5f - 0.35f * steps })
                    stepFoot = Mathf.Min(stepFoot, G(HW(da, u)) - 0.3f);
            for (int k = 1; k <= steps; k++)
            {
                float stepTop = deck - k * rise;
                if (stepTop > stepFoot) Kit.AddBox(hut, AtAbs(HW(0f, 3.5f - 0.35f * (k - 0.5f)), stepFoot), new Vector3(4f, stepTop - stepFoot, 0.35f), IsleKit.Wood, hutYaw);
            }
        }
        b.MeshPiece("Start Hut", hut.ToMesh(), snowy, true);
        Vector2 hutLocal = HW(0f, 5f) - new Vector2(site.x, site.z);
        b.ClearGround(10f, 10f, hutLocal.x, hutLocal.y);
        const float WallFace = 6.425f;      // the back wall's inner face, from the run's edge
        Piece hutBar = PartAbs("Progress Bar", HW(0f, WallFace - 0.09f), deck + 2.5f, hutYaw).Named("Hut Bar").Scaled(0.8f, 0.8f, 0.8f);
        hutBar.Set("PuzzleProgressBar", "total", 6);
        Piece clockBar = PartAbs("Progress Bar", HW(0f, WallFace - 0.11f), deck + 2.0f, hutYaw).Named("Clock Bar");
        Piece clock = PartAbs("Timer", HW(-1.1f, WallFace - 0.1f), deck + 1.35f, hutYaw).Named("Clock").Scaled(1.5f, 1.5f, 1.5f);
        PartAbs("Switch Start", HW(0.5f, WallFace - 0.073f), deck + 1.25f, hutYaw).Named("Hut Switch");
        PartAbs("Live Light", HW(0.5f, WallFace - 0.006f), deck + 1.42f, hutYaw).Named("Hut Live");
        Piece speaker = PartAbs("Intercom Speaker", HW(1.35f, WallFace - 0.1f), deck + 1.3f, hutYaw).Named("Hut Speaker");
        var eaves = new List<Vector3>();
        foreach (var (da, u) in new[] { (-2.3f, 3.05f), (2.3f, 3.05f), (2.3f, 6.95f), (-2.3f, 6.95f), (-2.3f, 3.05f) })
            eaves.Add(AtAbs(HW(da, u), deck + 2.98f));
        Kit.LightString(b, "Hut Lights", eaves, 0.1f, 0.5f, false, false, 43);

        // ================= the Judges' Cabin on the pad =================
        // the soundproof skin: slab, outer walls, roof and the door-frame cover, all under one PuzzleSoundProof parent
        Vector2 cabinMid = Pad(-3.3f, 0f);
        Piece cabin = b.Empty("Judges Cabin", cabinMid.x, cabinMid.y, Floor, false);
        Sdk.Set(b.AddScript(cabin, "PuzzleSoundProof"), "material", 0);
        var shell = new MeshBuilder();
        float wallLow = Floor - 0.05f, wallTop = Floor + 2.6f;
        PadBox(shell, -6.0f, -0.6f, -2.6f, 2.6f, -1.0f, Floor, IsleKit.Stone);                              // the floor slab, sunk 1 m
        PadBox(shell, -6.0f, -5.8f, -2.6f, 2.6f, wallLow, wallTop, IsleKit.Wood);                            // back (the mic's wall)
        PadBox(shell, -5.8f, -0.8f, -2.6f, -2.4f, wallLow, wallTop, IsleKit.Wood);                           // sides
        PadBox(shell, -5.8f, -0.8f, 2.4f, 2.6f, wallLow, wallTop, IsleKit.Wood);
        PadBox(shell, -0.8f, -0.6f, -2.6f, DoorZ0, wallLow, wallTop, IsleKit.Wood);                          // the door wall, round the door
        PadBox(shell, -0.8f, -0.6f, DoorZ1, 2.6f, wallLow, wallTop, IsleKit.Wood);
        PadBox(shell, -0.8f, -0.6f, DoorZ0, DoorZ1, Floor + DoorH, wallTop, IsleKit.Wood);
        // the door-frame cover: soundproof strips inside the door wall over the frame, up to the leaves' edges
        PadBox(shell, -0.86f, -0.8f, DoorZ0, DoorZ0 + Cover, Floor, Floor + DoorH, IsleKit.DarkWood);
        PadBox(shell, -0.86f, -0.8f, DoorZ1 - Cover, DoorZ1, Floor, Floor + DoorH, IsleKit.DarkWood);
        PadBox(shell, -0.86f, -0.8f, DoorZ0, DoorZ1, Floor + DoorH - Cover, Floor + DoorH, IsleKit.DarkWood);
        PadBox(shell, -6.15f, -0.45f, -2.75f, 2.75f, wallTop, wallTop + 0.2f, IsleKit.Red);                  // the roof
        Piece shellPiece = b.MeshPiece("Judges Cabin Shell", shell.ToMesh(), snowy, true);
        shellPiece.Transform.position = b.WorldPoint(0f, 0f, 0f, false);
        shellPiece.Under(cabin);
        // inside (not soundproof): the full-height partition with the turnstile's opening, closed above the gate, and a
        // return fin on its far face; and a step up to the door outside
        var fittings = new MeshBuilder();
        PadBox(fittings, -2.675f, -2.525f, -2.4f, GateZ0, Floor, wallTop, IsleKit.Cream);
        PadBox(fittings, -2.675f, -2.525f, GateZ0, GateZ1, Floor + 2.3f, wallTop, IsleKit.Cream);
        PadBox(fittings, -3.075f, -2.675f, GateZ0 - 0.1f, GateZ0, Floor, wallTop, IsleKit.Cream);
        PadBox(fittings, -0.6f, 0f, DoorZ0 - 0.1f, DoorZ1 + 0.1f, -0.4f, 0.15f, IsleKit.Stone);
        b.MeshPiece("Judges Cabin Fittings", fittings.ToMesh(), snowy, true);
        var frames = new MeshBuilder();      // a painted frame of each lamp's colour, so it reads while off
        for (int i = 0; i < 3; i++)
        {
            float z = -0.8f + 0.3f * i;
            PadBox(frames, -2.695f, -2.675f, z - 0.14f, z + 0.14f, Floor + 1.5f - 0.14f, Floor + 1.5f + 0.14f, LaneColours[i]);
        }
        b.MeshPiece("Judges Lamp Frames", frames.ToMesh(), bare, false);

        // the lamps on the partition's far face, facing into the judge's room (small panel lamps: 0.24 m, so they fit
        // 0.3 m apart; light reaching 0.4 m, every lamp 1.8 m or more from the outside)
        var lampComps = new Component[3];
        for (int i = 0; i < 3; i++)
        {
            Piece lamp = PadPart("Light 8", -2.795f, -0.8f + 0.3f * i, 1.5f, -90f).Named("Lamp " + LaneNames[i]);
            lamp.Set("PuzzleLight", "color", LampColours[i]).Set("PuzzleLight", "range", 0.4f);
            lampComps[i] = lamp.Component("PuzzleLight");
        }
        Piece judgeBar = PadPart("Progress Bar", -2.75f, -1.0f, 2.1f, -90f).Named("Judge Bar").Scaled(0.7f, 0.7f, 0.7f);
        judgeBar.Set("PuzzleProgressBar", "total", 6);
        Piece mic = PadPart("Intercom Mic", -5.78f, 0f, 1.4f, 90f).Named("Judge Mic");
        mic.Add("PuzzleIntercom", "speakers", speaker.Component("PuzzleIntercomSpeaker"));
        // a dim white room lamp while the puzzle runs (white tells nothing; it reaches no outer wall)
        Piece roomLamp = PadPart("Light 2", -4.3f, 0f, 2.6f, 0f).Named("Cabin Light");
        roomLamp.Set("PuzzleLight", "range", 1.5f);
        PadPart("Door", -0.7f, (DoorZ0 + DoorZ1) / 2f, 0f, 90f).Named("Cabin Door")
            .Set("PuzzleDoor", "mode", 1).Set("PuzzleDoor", "soundproof", true);      // shut while the puzzle runs
        PadPart("Gate No items", -2.6f, GateZ0 + 1.11f, 0f, -90f).Named("Judge Turnstile")      // its model runs -1.11 .. +1.01 m across
            .Set("PuzzleNoItemsGate", "oneWay", false).Set("PuzzleNoItemsGate", "blindMask", false).Set("PuzzleNoItemsGate", "defaultLocked", false);
        Piece dead = PadPart("Radio Dead Zone", -3.3f, 0f, 1.4f, 0f).Named("Cabin Dead Zone");
        BoxCollider deadBox = dead.Go.GetComponent<BoxCollider>();
        if (deadBox == null) deadBox = dead.Go.AddComponent<BoxCollider>();
        deadBox.size = new Vector3(5.6f, 3.2f, 5.4f);
        deadBox.center = Vector3.zero;
        // the judge's own quit, in the vestibule on the door wall's inner face (no turnstile between)
        PadPart("Switch Start", -0.873f, 1.6f, 1.25f, -90f).Named("Cabin Switch");
        PadPart("Live Light", -0.806f, 1.6f, 1.42f, -90f).Named("Cabin Live");
        var cabinLights = new List<Vector3>();
        foreach (var (x, z) in new[] { (-0.45f, -2.75f), (-0.45f, 2.75f), (-6.15f, 2.75f), (-6.15f, -2.75f), (-0.45f, -2.75f) })
        {
            Vector2 p = Pad(x, z);
            cabinLights.Add(new Vector3(p.x, Floor + 2.78f, p.y));
        }
        Kit.LightString(b, "Cabin Lights", cabinLights, 0.1f, 0.5f, false, false, 47);

        // the third quit point and the gourd, outside by the cabin, facing the run
        Vector2 corner = Pad(4f, 3f);
        IsleKit.StartCorner(b, corner.x, corner.y, padYaw - 90f);

        // ================= the clock, the three finals and the wiring =================
        clock.Set("PuzzleTimer", "duration", ClockSeconds);
        clock.Add("PuzzleTimer", "progressBars", clockBar.Component("PuzzleProgressBar"));
        Component clockC = clock.Component("PuzzleTimer");
        Component gantryC = gantryBar.Component("PuzzleProgressBar"), judgeC = judgeBar.Component("PuzzleProgressBar"), hutC = hutBar.Component("PuzzleProgressBar");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", clockC, "StartTimer");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStart", roomLamp.Component("PuzzleLight"), "TurnOn");
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", roomLamp.Component("PuzzleLight"), "TurnOff");
        foreach (var (group, two, three, four, rounds, name) in new[]
        {
            ("Two Rider Final", true, false, false, 4, "Final 4"),
            ("Three Rider Final", false, true, false, 6, "Final 6"),
            ("Four Rider Final", false, false, true, 8, "Final 8"),
        })
        {
            Piece lobby = IsleKit.Lobby(b, group, two, three, four);
            Piece order = b.Empty(name, 0f, 0f, 0.5f).Under(lobby);
            Component pio = b.AddScript(order, "PuzzlePressInOrder");
            Sdk.Add(pio, "lights", lampComps);
            Sdk.Add(pio, "buttons", gateComps);
            Sdk.Add(pio, "progressBars", gantryC, judgeC, hutC);
            Sdk.Set(pio, "rounds", rounds);
            Sdk.Set(pio, "holdAtEnd", false);
            b.RootPiece.On("PuzzleSDK", "onPuzzleStart", pio, "Begin");
            b.RootPiece.On("PuzzleSDK", "onPuzzleStop", pio, "End");
            clock.On("PuzzleTimer", "onFinished", pio, "End");
            Sdk.Wire(pio, "onEnd", clockC, "PauseTimer");
        }
        // the clock's reset last of all on a stop, after the finals' End (whose onEnd pauses it)
        b.RootPiece.On("PuzzleSDK", "onPuzzleStop", clockC, "ResetTimer");
        gantryBar.On("PuzzleProgressBar", "onFull", b.Puzzle, "Win");

        // ================= Calm Air: no jetpack thrust over the course =================
        // along 990 -> 1200, 45 m either side of the run's middle; level, so it reaches from 10 m under the lowest
        // ground under it to 150 m over the highest (the run falls tens of metres along it)
        Vector2 cMid = RC(CalmAlong), cDown = Down(CalmAlong), cRight = Right(cDown);
        float lowest = float.MaxValue, highestG = float.MinValue;
        for (float a = -CalmHalfAlong; a <= CalmHalfAlong + 0.01f; a += 7.5f)
            for (float u = -CalmHalfAcross; u <= CalmHalfAcross + 0.01f; u += 7.5f)
            {
                Vector2 p = cMid + cDown * a + cRight * u;
                if (!b.Ground.At(p.x, p.y, out float gy)) continue;
                lowest = Mathf.Min(lowest, gy);
                highestG = Mathf.Max(highestG, gy);
            }
        if (lowest == float.MaxValue) throw new Exception("BigAir: no ground under the Calm Air box");
        float calmBottom = lowest - CalmUnder, calmTop = highestG + CalmUp;
        IsleKit.CalmAir(b, "Big Air", cMid.x - site.x, cMid.y - site.z, new Vector3(2f * CalmHalfAcross, calmTop - calmBottom, 2f * CalmHalfAlong),
            YawOf(cDown), calmBottom - site.y, false);
        Debug.Log("[BWM] Big Air: Calm Air from " + Sdk.F(calmBottom) + " to " + Sdk.F(calmTop) + " m (ground " + Sdk.F(lowest) + " to " + Sdk.F(highestG)
            + "); gantry at along " + Sdk.F(gAlong) + ", masts " + Sdk.F(mastU) + " m out (run half-width " + Sdk.F(gHalf) + "); hut deck at "
            + Sdk.F(deck) + " m; site " + Sdk.F(site.x) + ", " + Sdk.F(site.z) + ", pad yaw " + Sdk.F(padYaw));
    }
}
