using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RagnarockEditor.VR
{
    internal static class FjordWorld
    {
        private static Mesh _snow;
        private static Material _sky;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= Configure;
            SceneManager.sceneLoaded += Configure;
        }

        private static void Configure(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                // Existing world-space canvases are also used by the legacy BPM/seek UI.
                foreach (var slider in root.GetComponentsInChildren<Slider>(true))
                {
                    var canvas = slider.GetComponentInParent<Canvas>(true);
                    if (!canvas || canvas.renderMode != RenderMode.WorldSpace)
                        continue;
                    var rect = (RectTransform)slider.transform;
                    var box = slider.GetComponent<BoxCollider>();
                    if (!box) box = slider.gameObject.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = rect.rect.center;
                    box.size = new Vector3(rect.rect.width, rect.rect.height, 1f);
                }
                if (root.name != "Environment") continue;
                var ground = root.transform.Find("Ground");
                if (!ground || !ground.TryGetComponent<MeshFilter>(out var filter)) continue;
                if (!_snow) _snow = SnowCap();
                filter.sharedMesh = _snow;
                ground.localScale = Vector3.one;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogDensity = .055f;
                var panorama = RenderSettings.skybox;
                if (panorama && panorama.HasProperty("_MainTex") && panorama.GetTexture("_MainTex"))
                {
                    if (!_sky) _sky = new Material(Resources.Load<Shader>("FjordHorizon"));
                    _sky.mainTexture = panorama.GetTexture("_MainTex");
                    if (panorama.HasProperty("_Exposure")) _sky.SetFloat("_Exposure", panorama.GetFloat("_Exposure"));
                    if (panorama.HasProperty("_Rotation")) _sky.SetFloat("_Rotation", panorama.GetFloat("_Rotation"));
                    RenderSettings.skybox = _sky;
                }
            }
        }

        private static Mesh SnowCap()
        {
            const int slices = 96;
            float[] rings = { 48f, 56f, 66f, 78f, 92f };
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            for (int k = 0; k < rings.Length; k++)
            {
                float r = rings[k];
                float outerDistance = Mathf.Max(0f, r - rings[0]);
                float drop = outerDistance * outerDistance * .006f;
                for (int j = 0; j < slices; j++)
                {
                    float a = j * 2f * Mathf.PI / slices;
                    vertices.Add(new Vector3(r * Mathf.Cos(a), -drop, r * Mathf.Sin(a)));
                    int ringStart = 1 + k * slices;
                    int i = ringStart + j;
                    int next = ringStart + (j + 1) % slices;
                    triangles.Add(k == 0 ? 0 : i - slices);
                    triangles.Add(next);
                    triangles.Add(i);
                    if (k > 0)
                    {
                        triangles.Add(i - slices);
                        triangles.Add(next - slices);
                        triangles.Add(next);
                    }
                }
            }
            var mesh = new Mesh { name = "Fjord curved snowfield" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
