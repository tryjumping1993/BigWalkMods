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
        HowTo(b, cx, cz, yaw, baseHeight, onGround);
    }

    // The how-to-play board over the start switch: the puzzle's own picture (tools/howto_art.py paints one a puzzle
    // from docs/puzzle-variety/howto.json into Assets/Textures/HowTo/<Name>.png: its name, how many players, and what
    // to do in up to five lines), 1.8 x 1.2 m between two posts, its foot 1.8 m up so the switch, the live lamp and
    // the gourd stay clear under it. A Sign's text does not show in the game, and until 2026-11-24 no puzzle said
    // anything about itself (players: the puzzles "aren't intuitive to human players"). A puzzle with no picture
    // gets no board.
    public const float HowToWidth = 1.8f, HowToFoot = 1.8f, HowToTop = 3.0f;
    public static void HowTo(PuzzleBuilder b, float cx, float cz, float yaw, float baseHeight = 0f, bool onGround = true)
    {
        string path = "Assets/Textures/HowTo/" + b.Name + ".png";
        var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (importer == null) { Debug.Log("[BWM] " + b.Name + ": no how-to-play board (" + path + " is missing: tools/howto_art.py makes it from docs/puzzle-variety/howto.json)"); return; }
        if (importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled || importer.alphaIsTransparency || importer.maxTextureSize < 1024)
        { importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = true; importer.alphaIsTransparency = false; importer.maxTextureSize = 1024; importer.SaveAndReimport(); }
        var picture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (picture == null) return;

        // the frame: the switch's own post carried on up, a second post left of it, a backing board and a little roof
        const float left = -0.32f, right = 1.6f;
        float mid = (left + right) / 2f;
        var frame = new MeshBuilder();
        Vector2 a = Turn(left, 0f, yaw), c = Turn(right, 0f, yaw), m = Turn(mid, 0f, yaw), roof = Turn(mid, 0.04f, yaw);
        Kit.AddBox(frame, new Vector3(a.x, 0f, a.y), new Vector3(0.14f, HowToTop + 0.1f, 0.14f), Wood, yaw);
        Kit.AddBox(frame, new Vector3(c.x, 1.5f, c.y), new Vector3(0.14f, HowToTop + 0.1f - 1.5f, 0.14f), Wood, yaw);
        Kit.AddBox(frame, new Vector3(m.x, HowToFoot - 0.04f, m.y), new Vector3(HowToWidth + 0.08f, HowToTop - HowToFoot + 0.08f, 0.06f), DarkWood, yaw);
        Kit.AddBox(frame, new Vector3(roof.x, HowToTop + 0.1f, roof.y), new Vector3(HowToWidth + 0.5f, 0.08f, 0.5f), DarkWood, yaw);
        b.MeshPiece("How To Frame", frame.ToMesh(), Snowy(b)).Transform.position = b.WorldPoint(cx, cz, baseHeight, onGround);

        // the picture, a hair in front of the backing, read from the side the switch faces
        var face = new MeshBuilder();
        Vector2 l = Turn(mid + HowToWidth / 2f, 0.036f, yaw), r = Turn(mid - HowToWidth / 2f, 0.036f, yaw);      // a reader's left is the corner's +x
        Color white = Color.white;
        face.QuadUV(new Vector3(l.x, HowToFoot, l.y), new Vector3(r.x, HowToFoot, r.y), new Vector3(r.x, HowToTop, r.y), new Vector3(l.x, HowToTop, l.y),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), white, white, white, white);
        Material paint = b.ShaderMaterial("BWM/Picture", "How To", mat => { mat.SetTexture("_MainTex", picture); mat.SetFloat("_Indoor", 0f); mat.SetFloat("_Shine", 0.15f); mat.SetFloat("_Cull", 0f); });
        b.MeshPiece("How To Board", face.ToMesh(), paint, false).Transform.position = b.WorldPoint(cx, cz, baseHeight, onGround);
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

    // Words on one of the SDK's Sign parts
    public static void SignText(Piece sign, string text, float size)
    {
        var tmp = sign.Go.GetComponentInChildren<TMPro.TextMeshPro>(true);
        if (tmp == null)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(sign.Go.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(1.5f, 0.6f);
        }
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = new Color(0.98f, 0.92f, 0.8f);
    }

    // A teleporter (Winter Walk's Teleporter): a red signboard on posts at (x, z) facing 'yaw', and 1.4 m in front of
    // it a gold pad on the ground. Whoever stands on the pad a moment is set down at the object named
    // "Teleport Arrival <place>" (TeleportArrival makes one). 'name' tells this pad from the others.
    public static void TeleportBooth(PuzzleBuilder b, string name, string place, string words, float x, float z, float yaw, bool onGround = true)
    {
        BoardOnPosts(b, name + " Teleporter", x, z, yaw, 1.8f, 1.5f, 2.2f, Red, onGround);
        Vector2 face = Turn(0f, FaceZ + 0.06f, yaw), front = Turn(0f, 1.4f, yaw);
        SignText(b.Part("Sign", x + face.x, z + face.y, 1.85f, yaw, onGround).Named(name + " Teleporter Words"), words, 2.2f);
        var mb = new MeshBuilder();
        Kit.AddDisc(mb, Vector3.zero, 0.95f, 0.04f, DarkWood, 28);
        Kit.AddDisc(mb, new Vector3(0f, 0.04f, 0f), 0.85f, 0.02f, Gold, 28);
        Kit.AddDisc(mb, new Vector3(0f, 0.06f, 0f), 0.55f, 0.01f, Red, 24);
        Kit.AddDisc(mb, new Vector3(0f, 0.07f, 0f), 0.3f, 0.01f, Gold, 20);
        Piece pad = b.MeshPiece(name + " Teleporter Pad", mb.ToMesh(), Bare(b), false);
        pad.Transform.position = b.WorldPoint(x + front.x, z + front.y, 0f, onGround);
        b.Empty("Teleport Pad " + name + " to " + place, x + front.x, z + front.y, 0.05f, onGround);
        b.Empty("Room Light Night " + name + " Pad FFCB8C 5.0 1.1", x + face.x * 3f, z + face.y * 3f, 2.0f, onGround);
    }

    // Where a teleporter's travellers arrive, facing 'yaw'
    public static void TeleportArrival(PuzzleBuilder b, string place, float x, float z, float yaw, bool onGround = true)
    {
        Piece at = b.Empty("Teleport Arrival " + place, x, z, 0.05f, onGround);
        at.Transform.rotation = Quaternion.Euler(0f, b.Root.transform.eulerAngles.y + yaw, 0f);
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

    // Which way a one-way turnstile lets players through: IN at the side the part faces (its local +z) and OUT at its
    // back, so a gate placed with yaw 0 is walked from north to south. Until 2026-11-24 every one-way gate here was
    // placed on the opposite belief ("front is -z") and stood backwards: at Thin Ice the blindfolded crosser could
    // not get onto the ice (the user, playing it with friends), and the three "way out" gates let in, not out.
    // OneWay gives the place and yaw for a gate that is to be walked the way 'yaw' points (yaw 0: south to north):
    // the part turned half round, and moved 10 cm so it stays in the middle of its doorway (its pivot is 5 cm off
    // its middle).
    public static (float x, float z, float yaw) OneWay(float x, float z, float yaw)
    {
        Vector2 shift = Turn(0.1f, 0f, yaw);
        return (x - shift.x, z - shift.y, yaw + 180f);
    }

    // The blind turnstile: only a player wearing the blindfold passes, one way, walking the way 'yaw' points
    // (yaw 0: from the -z side to the +z side)
    public static Piece BlindGate(PuzzleBuilder b, float x, float z, float yaw, float baseHeight = 0f, bool onGround = true)
    {
        (float gx, float gz, float gyaw) = OneWay(x, z, yaw);
        return b.Part("Gate No items", gx, gz, baseHeight, gyaw, onGround).Named("Blind Turnstile").Set("PuzzleNoItemsGate", "blindMask", true).Set("PuzzleNoItemsGate", "oneWay", true);
    }

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
