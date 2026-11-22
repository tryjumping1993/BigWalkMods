// Winter Walk: real light for rooms in the packs.
//
// A pack marks where its lamps hang with empty objects named "Room Light <anything>" (the summit pizzeria's pendant
// lamps, its kitchen tubes). This puts a point light at each, lit only while this player's camera is within 70 m, so
// a room is lit from its own lamps rather than only by its glowing shades. The pack's BWM/Picture and BWM/Snowy
// materials take the lights. The marker's name says what light: a six-digit hex colour (sRGB), then its range and its
// intensity, as in "Room Light 3 FFBE78 4.5 1.6"; without them it is warm (or "cold": a cool white tube light; "red":
// an exit sign's glow) at the default range and intensity. Intensity and Range scale every light live.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WinterWalk
{
    public class RoomLights : MonoBehaviour
    {
        public RoomLights(IntPtr ptr) : base(ptr) { }

        public static string State = "not loaded";
        public static float Intensity = 1f, Range = 1f;      // multipliers, can be set live with the companion

        readonly List<Light> lights = new List<Light>();
        readonly List<Vector3> places = new List<Vector3>();
        readonly List<float> ranges = new List<float>(), powers = new List<float>();
        float setIntensity = 1f, setRange = 1f;
        float nextLook, nextCheck;
        bool found;

        void Find()
        {
            foreach (Light l in lights) if (l != null) Destroy(l.gameObject);
            lights.Clear(); places.Clear(); ranges.Clear(); powers.Clear();
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (t == null || !t.name.StartsWith("Room Light")) continue;
                var go = new GameObject("Lamp for " + t.name);
                go.transform.position = t.position;
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                string n = t.name.ToLowerInvariant();
                Color colour = n.Contains("cold") ? new Color(0.88f, 0.93f, 1f) : n.Contains("red") ? new Color(1f, 0.2f, 0.15f) : new Color(1f, 0.78f, 0.5f);
                float range = n.Contains("red") ? 3f : 7.5f, power = n.Contains("red") ? 0.8f : 2.2f;
                int numbers = 0;
                string[] words = t.name.Split(' ');
                for (int w = 2; w < words.Length; w++)
                {
                    string word = words[w];
                    if (word.Length == 6 && IsHex(word))
                        colour = new Color(Convert.ToInt32(word.Substring(0, 2), 16) / 255f, Convert.ToInt32(word.Substring(2, 2), 16) / 255f, Convert.ToInt32(word.Substring(4, 2), 16) / 255f);
                    else if (word.IndexOf('.') >= 0 && float.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                    {
                        if (numbers == 0) range = f; else power = f;
                        numbers++;
                    }
                }
                light.color = colour;
                light.range = range * Range;
                light.intensity = power * Intensity;
                ranges.Add(range); powers.Add(power);
                light.shadows = LightShadows.None;
                light.enabled = false;
                lights.Add(light);
                places.Add(t.position);
            }
            setIntensity = Intensity; setRange = Range;
            found = lights.Count > 0;
            State = found ? lights.Count + " room lights" : "no room lights in the loaded packs";
            if (found) Plugin.L.LogInfo("Winter Walk: " + State);
        }

        static bool IsHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        void Update()
        {
            using var diag = Diag.Time("lights");
            try
            {
                if (!found)
                {
                    if (Time.unscaledTime < nextLook) return;
                    nextLook = Time.unscaledTime + 5f;
                    Find();
                    return;
                }
                if (Time.unscaledTime < nextCheck) return;
                nextCheck = Time.unscaledTime + 0.5f;
                Camera cam = Camera.main;
                Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
                bool rescale = setIntensity != Intensity || setRange != Range;
                for (int i = 0; i < lights.Count; i++)
                {
                    if (lights[i] == null) { found = false; return; }
                    bool on = cam != null && (places[i] - eye).sqrMagnitude < 70f * 70f;
                    if (lights[i].enabled != on) lights[i].enabled = on;
                    if (rescale) { lights[i].intensity = powers[i] * Intensity; lights[i].range = ranges[i] * Range; }
                }
                setIntensity = Intensity; setRange = Range;
            }
            catch (Exception e)
            {
                State = "stopped: " + e.GetType().Name + ": " + e.Message;
                found = false;
                nextLook = Time.unscaledTime + 30f;
            }
        }
    }
}
