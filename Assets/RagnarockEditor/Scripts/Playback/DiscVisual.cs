using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Общий визуал «кружка» (руны и эффект удара): плоский цилиндр-диск,
    /// двухсторонний прозрачный материал URP, цвет по номеру барабана.
    ///
    /// Размеры задаются в метрах и не зависят от масштаба префаба. Прозрачность
    /// нужна для плавного появления/исчезновения на границах полёта — размер диска
    /// при этом не меняется.
    /// </summary>
    public static class DiscVisual
    {
        /// <summary>Радиус диска в метрах (диаметр 0.32 м при диаметре барабана 0.35 м).</summary>
        public const float Radius = 0.16f;

        /// <summary>Толщина диска в метрах.</summary>
        public const float Thickness = 0.02f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        private static readonly Color[] DrumColors =
        {
            new Color(0.95f, 0.25f, 0.28f, 1f),
            new Color(0.25f, 0.55f, 1.00f, 1f),
            new Color(0.30f, 0.90f, 0.45f, 1f),
            new Color(1.00f, 0.80f, 0.20f, 1f)
        };

        private static Mesh _mesh;
        private static Material _material;

        /// <summary>Меш диска (радиус 0.5, высота 1 в локальных координатах).</summary>
        public static Mesh Mesh
        {
            get
            {
                if (_mesh == null)
                    _mesh = BuildDiscMesh();

                return _mesh;
            }
        }

        /// <summary>Материал диска (общий, цвет и прозрачность — через MaterialPropertyBlock).</summary>
        public static Material Material
        {
            get
            {
                if (_material == null)
                    _material = BuildMaterial();

                return _material;
            }
        }

        /// <summary>Цвет руны/эффекта по индексу барабана (0-3).</summary>
        public static Color DrumColor(int drumIndex)
        {
            int index = drumIndex % DrumColors.Length;
            if (index < 0)
                index += DrumColors.Length;

            return DrumColors[index];
        }

        /// <summary>
        /// Локальный масштаб, дающий нужный мировой размер при заданном масштабе родителя.
        /// </summary>
        public static Vector3 ScaleForWorldSize(float radius, float thickness, float parentScale)
        {
            float scale = Mathf.Max(0.0001f, Mathf.Abs(parentScale));
            return new Vector3(radius / (0.5f * scale), thickness / scale, radius / (0.5f * scale));
        }

        /// <summary>Создаёт/обновляет рендерер диска на объекте и красит его под барабан.</summary>
        public static MeshRenderer Create(GameObject host, int drumIndex)
        {
            var filter = host.GetComponent<MeshFilter>();
            if (filter == null)
                filter = host.AddComponent<MeshFilter>();

            filter.sharedMesh = Mesh;

            var renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = host.AddComponent<MeshRenderer>();

            renderer.sharedMaterial = Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ApplyColor(renderer, DrumColor(drumIndex), 1f);
            return renderer;
        }

        /// <summary>
        /// Красит рендерер и задаёт прозрачность (0..1) через MaterialPropertyBlock.
        /// </summary>
        public static void ApplyColor(MeshRenderer renderer, Color color, float alpha = 1f)
        {
            if (renderer == null)
                return;

            color.a = Mathf.Clamp01(alpha);

            renderer.GetPropertyBlock(Block);
            Block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(Block);
        }

        // ------------------------------------------------------------ построение

        private static Mesh BuildDiscMesh()
        {
            const int segments = 32;
            const float radius = 0.5f;
            const float halfHeight = 0.5f;

            var vertices = new List<Vector3>(segments * 4 + 2);
            var normals = new List<Vector3>(segments * 4 + 2);
            var triangles = new List<int>(segments * 12);

            // верхняя крышка
            vertices.Add(new Vector3(0f, halfHeight, 0f));
            normals.Add(Vector3.up);
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, halfHeight, Mathf.Sin(angle) * radius));
                normals.Add(Vector3.up);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.Add(0);
                triangles.Add(1 + i);
                triangles.Add(1 + next);
            }

            // нижняя крышка
            int bottomCenter = vertices.Count;
            vertices.Add(new Vector3(0f, -halfHeight, 0f));
            normals.Add(Vector3.down);

            int bottomRing = vertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, -halfHeight, Mathf.Sin(angle) * radius));
                normals.Add(Vector3.down);
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.Add(bottomCenter);
                triangles.Add(bottomRing + next);
                triangles.Add(bottomRing + i);
            }

            // боковая поверхность
            int sideStart = vertices.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                vertices.Add(direction * radius + Vector3.up * halfHeight);
                vertices.Add(direction * radius - Vector3.up * halfHeight);
                normals.Add(direction);
                normals.Add(direction);
            }

            for (int i = 0; i < segments; i++)
            {
                int current = sideStart + i * 2;
                int next = sideStart + ((i + 1) % segments) * 2;

                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + 1);

                triangles.Add(next);
                triangles.Add(next + 1);
                triangles.Add(current + 1);
            }

            var mesh = new Mesh { name = "RuneDisc" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            // UI/Default и часть fallback-шейдеров требуют UV; текстура белая, значения не важны
            mesh.SetUVs(0, new List<Vector2>(vertices.Count));
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material BuildMaterial()
        {
            // Direct resource reference retains the transparent, stereo-capable pass in Quest builds.
            return new Material(Resources.Load<Shader>("FjordRune")) { name = "RuneDisc_Runtime" };
        }
    }
}
