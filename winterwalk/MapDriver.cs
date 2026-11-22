// Winter Walk: the Winter Isle on the game's folding map.
//
// The game's map shows only its own island. This stamps a close-up of the Winter Isle (a picture from the Island
// Gear pack, on a hidden object "Map Overlay", drawn by tools/islemap.py) onto a stretch of open sea on it: the
// map's picture ("_Albedo" of the material "PaperMap") is copied into a texture of ours with the close-up drawn
// over part of it, and the material is given that instead. Checked every few seconds, so maps that appear later, or
// a picture the game puts back, are stamped again. Where it goes can be moved live (X, Y, W, H, FlipX, FlipY,
// then Again = true).
using System;
using UnityEngine;

namespace WinterWalk
{
    public class MapDriver : MonoBehaviour
    {
        public MapDriver(IntPtr ptr) : base(ptr) { }

        public static float X = 0.69f, Y = 0.7f, W = 0.28f, H = 0.225f;      // of the map's picture, from its lower left
        public static bool FlipX, FlipY, Again;
        public static string State = "not loaded";

        RenderTexture stamped;
        Material stamp;
        float nextLook;

        void Update()
        {
            using var diag = Diag.Time("map");
            if (Time.unscaledTime < nextLook && !Again) return;
            nextLook = Time.unscaledTime + 4f;
            try
            {
                if (stamp == null)
                {
                    GameObject overlay = GameObject.Find("Map Overlay");
                    Renderer r = overlay != null ? overlay.GetComponent<Renderer>() : null;
                    if (r == null || r.sharedMaterial == null) { State = "no close-up loaded (the Island Gear pack)"; return; }
                    stamp = r.sharedMaterial;
                }
                int done = 0;
                foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Material>()))
                {
                    Material m = o != null ? o.TryCast<Material>() : null;
                    if (m == null || m.shader == null || !m.shader.name.Contains("PaperMap") || !m.HasProperty("_Albedo")) continue;
                    Texture picture = m.GetTexture("_Albedo");
                    if (picture == null) continue;
                    if (stamped != null && picture.Pointer == stamped.Pointer && !Again) { done++; continue; }
                    if (stamped != null && picture.Pointer == stamped.Pointer) continue;      // (restamping: only from the game's own picture)
                    if (stamped == null)
                    {
                        stamped = new RenderTexture(picture.width, picture.height, 0, RenderTextureFormat.ARGB32);
                        stamped.name = "PaperMap with the Winter Isle";
                        stamped.useMipMap = true;
                        stamped.autoGenerateMips = true;
                        stamped.filterMode = FilterMode.Trilinear;
                        stamped.anisoLevel = 8;
                        original = picture;
                    }
                    Draw();
                    m.SetTexture("_Albedo", stamped);
                    done++;
                }
                if (Again && original != null) Draw();
                Again = false;
                State = done > 0 ? "stamped on " + done + " map material(s) at " + X + ", " + Y + " size " + W + " x " + H : "no map found yet";
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                nextLook = Time.unscaledTime + 30f;
            }
        }

        Texture original;
        void Draw()
        {
            stamp.SetVector("_Rect", new Vector4(X, Y, W, H));
            stamp.SetVector("_Flip", new Vector4(FlipX ? 1f : 0f, FlipY ? 1f : 0f, 0f, 0f));
            Graphics.Blit(original, stamped);
            Graphics.Blit(stamp.mainTexture, stamped, stamp);
        }
    }
}
