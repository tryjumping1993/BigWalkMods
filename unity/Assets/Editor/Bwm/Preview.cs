// Renders a built puzzle inside the SDK's reference map to PNG files, so a build can be looked at without
// starting the game. The SDK's parts show as the game's own models. A preview never fails a build.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Bwm
{
    public static class Preview
    {
        public static void Render(GameObject root, string name, string outDir, float radius)
        {
            var temp = new List<UnityEngine.Object>();
            try
            {
                Type previews = Sdk.T("GamePreviews");
                FieldInfo next = previews.GetField("nextTick", Sdk.Any);
                if (next != null) next.SetValue(null, 0.0);
                Sdk.Call(previews, null, "Tick");      // the game's models, standing in for the SDK's marker objects

                var lightGo = new GameObject("BwmPreviewLight") { hideFlags = HideFlags.HideAndDontSave };
                temp.Add(lightGo);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                var camGo = new GameObject("BwmPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
                temp.Add(camGo);
                Camera cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.Skybox;

                Vector3 c = root.transform.position + Vector3.up;
                float r = Mathf.Max(radius, 6f);
                Quaternion q = root.transform.rotation;
                var shots = new (string tag, Vector3 offset)[]
                {
                    ("front", q * new Vector3(0f, r * 0.45f, -r * 1.3f)),
                    ("side", q * new Vector3(r * 1.3f, r * 0.6f, -r * 0.7f)),
                    ("top", new Vector3(0.01f, r * 2.2f, -0.01f)),
                    ("wide", q * new Vector3(-r * 4f, r * 3f, -r * 5f)),
                };
                Directory.CreateDirectory(outDir);
                foreach (var shot in shots)
                {
                    camGo.transform.position = c + shot.offset;
                    camGo.transform.LookAt(c);
                    var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                    temp.Add(rt);
                    if (GraphicsSettings.currentRenderPipeline != null)
                        RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    temp.Add(tex);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    string file = Path.Combine(outDir, name + "-" + shot.tag + ".png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    Debug.Log("[BWM] Preview: " + file);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BWM] Preview skipped: " + Sdk.Unwrap(e));
            }
            finally
            {
                foreach (UnityEngine.Object o in temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
