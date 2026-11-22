// Shared pieces for the Winter Isle's puzzles: their colours, the corner every puzzle has (the start switch on a
// post with its live lamp, the gourd's vice on a stone, the pillow and the confetti), boards on posts, and lobby
// size groups.
using Bwm;
using UnityEngine;

public static class IsleKit
{
    public static readonly Color Wood = new Color(0.36f, 0.25f, 0.17f);
    public static readonly Color DarkWood = new Color(0.24f, 0.16f, 0.11f);
    public static readonly Color Board = new Color(0.20f, 0.22f, 0.28f);
    public static readonly Color Stone = new Color(0.50f, 0.52f, 0.56f);
    public static readonly Color Red = new Color(0.78f, 0.12f, 0.1f);
    public static readonly Color Green = new Color(0.1f, 0.42f, 0.2f);
    public static readonly Color Gold = new Color(0.95f, 0.72f, 0.22f);
    public static readonly Color Cream = new Color(0.9f, 0.86f, 0.76f);
    public static readonly Color Ice = new Color(0.7f, 0.86f, 0.95f);

    // A local offset (x across, z forward) turned by a yaw in degrees, as Unity turns things about up
    public static Vector2 Turn(float x, float z, float yaw)
    {
        float a = yaw * Mathf.Deg2Rad;
        return new Vector2(x * Mathf.Cos(a) + z * Mathf.Sin(a), -x * Mathf.Sin(a) + z * Mathf.Cos(a));
    }

    public static Material Snowy(PuzzleBuilder b) => b.ShaderMaterial("BWM/Snowy", "Snowy");
    // painted things that never take snow (bells, baubles, signs)
    public static Material Bare(PuzzleBuilder b) => b.ShaderMaterial("BWM/Snowy", "Bare", m => m.SetFloat("_SnowLine", 2f));
    public static Material Fir(PuzzleBuilder b) => b.ShaderMaterial("BWM/Fir", "Fir", m => Kit.SetTreeMaps(m, "FirBark"));

    // The start corner at (cx, cz) facing 'yaw': a post with the start switch and the live lamp on its front, a
    // stone with the gourd's vice to its left (unless the puzzle keeps its gourd in a Box), the pillow beyond it
    // and the confetti cannon in front
    public static void StartCorner(PuzzleBuilder b, float cx, float cz, float yaw, bool vice = true, float baseHeight = 0f, bool onGround = true)
    {
        var mb = new MeshBuilder();
        Vector2 post = Turn(1.6f, 0f, yaw), stone = Turn(-1.2f, 0f, yaw);
        Kit.AddBox(mb, new Vector3(post.x, 0f, post.y), new Vector3(0.26f, 1.5f, 0.2f), Wood, yaw);
        if (vice) Kit.AddBox(mb, new Vector3(stone.x, 0f, stone.y), new Vector3(1.1f, 0.45f, 1.1f), Stone, yaw);
        b.MeshPiece("Start Corner", mb.ToMesh(), Snowy(b)).Transform.position = b.WorldPoint(cx, cz, baseHeight, onGround);
        Vector2 sw = Turn(1.6f, 0.173f, yaw), live = Turn(1.6f, 0.1f, yaw), confetti = Turn(0f, 1.2f, yaw), pillow = Turn(-2.8f, 0f, yaw);
        b.Part("Switch Start", cx + sw.x, cz + sw.y, baseHeight + 1.25f, yaw, onGround);
        b.Part("Live Light", cx + live.x, cz + live.y, baseHeight + 1.42f, yaw, onGround);
        if (vice) b.Part("Gourd Vice", cx + stone.x, cz + stone.y, baseHeight + 0.45f, yaw, onGround);
        b.Part("Pillow", cx + pillow.x, cz + pillow.y, baseHeight + 0.13f, 0f, onGround);
        b.Part("Confetti", cx + confetti.x, cz + confetti.y, baseHeight + 0.2f, 0f, onGround);
    }

    // A board on two posts at (cx, cz) facing 'yaw' (its face looks along +z turned by yaw), 'width' wide, its face
    // from 'bottom' to 'top' above the ground, with a little roof against the snow; parts go on its face at
    // FaceZ (0.08 m in front of the posts' middle)
    public const float FaceZ = 0.1f;
    public static Piece BoardOnPosts(PuzzleBuilder b, string name, float cx, float cz, float yaw, float width, float bottom, float top, Color face, bool onGround = true)
    {
        var mb = new MeshBuilder();
        Vector2 left = Turn(-width / 2f - 0.12f, 0f, yaw), right = Turn(width / 2f + 0.12f, 0f, yaw);
        Kit.AddBox(mb, new Vector3(left.x, 0f, left.y), new Vector3(0.2f, top + 0.3f, 0.2f), Wood, yaw);
        Kit.AddBox(mb, new Vector3(right.x, 0f, right.y), new Vector3(0.2f, top + 0.3f, 0.2f), Wood, yaw);
        Kit.AddBox(mb, new Vector3(0f, bottom, 0f), new Vector3(width, top - bottom, 0.12f), face, yaw);
        Vector2 roof = Turn(0f, 0.05f, yaw);
        Kit.AddBox(mb, new Vector3(roof.x, top + 0.3f, roof.y), new Vector3(width + 0.7f, 0.12f, 0.6f), DarkWood, yaw);
        Piece piece = b.MeshPiece(name, mb.ToMesh(), Snowy(b));
        piece.Transform.position = b.WorldPoint(cx, cz, 0f, onGround);
        return piece;
    }

    // The blindfold on a hook on a post at (x, z), its hook facing 'yaw', for the blind-mask puzzles: the game's own
    // blindfold, which the blind turnstile needs a player to wear; it can be hung back on the hook
    public static void BlindfoldHook(PuzzleBuilder b, float x, float z, float yaw, float baseHeight = 0f, bool onGround = true, string tag = "")
    {
        var post = new MeshBuilder();
        Kit.AddBox(post, Vector3.zero, new Vector3(0.18f, 1.9f, 0.18f), Wood, yaw);
        b.MeshPiece("Blindfold Post" + tag, post.ToMesh(), Snowy(b)).Transform.position = b.WorldPoint(x, z, baseHeight, onGround);
        Vector2 front = Turn(0f, 0.4f, yaw), face = Turn(0f, 0.12f, yaw);
        Piece blindfold = b.Part("Vanilla Item", x + front.x, z + front.y, baseHeight + 1.3f, yaw, onGround).Named("Blindfold")
            .Set("PuzzleVanillaItem", "itemType", 0).Set("PuzzleVanillaItem", "savesOnReload", false);
        Piece hook = b.Part("Hook", x + face.x, z + face.y, baseHeight + 1.55f, yaw, onGround).Named("Blindfold Hook");
        hook.Set("PuzzleHook", "defaultItem", blindfold.Component("PuzzleVanillaItem"));
        Sdk.SetFlags(hook.Component("PuzzleHook"), "acceptedVanilla", 1);
    }

    // The blind turnstile: only a player wearing the blindfold passes, one way, front (-z turned by yaw) to back
    public static Piece BlindGate(PuzzleBuilder b, float x, float z, float yaw, float baseHeight = 0f, bool onGround = true)
        => b.Part("Gate No items", x, z, baseHeight, yaw, onGround).Named("Blind Turnstile").Set("PuzzleNoItemsGate", "blindMask", true).Set("PuzzleNoItemsGate", "oneWay", true);

    // An empty parent whose children exist only in the lobby sizes given
    public static Piece Lobby(PuzzleBuilder b, string name, bool two, bool three, bool four)
    {
        Piece group = b.Empty(name, 0f, 0f, 0f);
        Component hider = b.AddScript(group, "PuzzleHider");
        Sdk.Set(hider, "show2", two);
        Sdk.Set(hider, "show3", three);
        Sdk.Set(hider, "show4", four);
        return group;
    }

    // Calm Air over a puzzle: an empty named "Calm Air <name>" at (cx, cz), turned by 'yaw' (as parts are, about the
    // root), its scale the box's size (x across, y up, z forward), its bottom 'baseHeight' above the ground (or the
    // root's level with onGround false), so its centre is size.y / 2 higher. Winter Walk's jetpacks do not fire inside
    // it (JetpackDriver). No collider, no renderer. Start it a few metres under the ground so nobody slips in under it,
    // and make it tall: falling in from above with no thrust still works, so walls must stand well inside its edges.
    public static Piece CalmAir(PuzzleBuilder b, string name, float cx, float cz, Vector3 size, float yaw = 0f, float baseHeight = 0f, bool onGround = true)
    {
        Piece box = b.Empty("Calm Air " + name, cx, cz, baseHeight + size.y / 2f, onGround);
        box.Transform.rotation = b.Root.transform.rotation * Quaternion.Euler(0f, yaw, 0f);
        box.Transform.localScale = size;
        return box;
    }

    // A Presence Sensor box (players standing in it, unless set otherwise) at (x, z), 'size' across and 2.6 m tall
    public static Piece Sensor(PuzzleBuilder b, string name, float x, float z, Vector3 size, float baseHeight = 0f, bool onGround = true)
    {
        Piece zone = b.Part("Presence Sensor", x, z, baseHeight + size.y / 2f, 0f, onGround).Named(name);
        BoxCollider box = zone.Go.GetComponent<BoxCollider>();
        if (box == null) box = zone.Go.AddComponent<BoxCollider>();
        box.size = size;
        box.center = Vector3.zero;
        return zone;
    }
}
