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
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

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
            Block.SetColor(ColorId, color);

            // свечение гасим вместе с прозрачностью, иначе диск «светится» уже невидимым
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(EmissionColorId))
                Block.SetColor(EmissionColorId, color * (0.65f * color.a));

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
            // Lit идёт первым: он точно есть в билде (его используют материалы сцены),
            // поэтому Shader.Find не вернёт null даже после стриппинга шейдеров.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader) { name = "RuneDisc_Runtime" };

            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, Color.white);

            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, Color.white);

            // диск виден с обеих сторон
            if (material.HasProperty(CullId))
                material.SetFloat(CullId, 0f);

            // прозрачность: плавное появление/исчезновение вместо изменения размера.
            // Blend/ZWrite выставляем даже если свойства _Surface нет (fallback-шейдер).
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);

            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);

            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            if (material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, Color.white);
            }

            return material;
        }
    }
}
