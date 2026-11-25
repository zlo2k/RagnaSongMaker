using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Каменная плита для uGUI: гранёный контур со сколом, фаска по краю,
/// шероховатость грани и необязательная утопленная площадка (углубление).
///
/// Плита целиком строится вершинами, поэтому текстуры не нужны: цвет задаётся
/// через <see cref="Graphic.color"/>, размер фаски/углубления — через <see cref="Configure"/>.
/// Так выглядят все панели и кнопки интерфейса — «из каменных плит».
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
[RequireComponent(typeof(RectTransform))]
public sealed class StonePlate : MaskableGraphic
{
    /// <summary>
    /// CanvasRenderer обязателен: без него плита не рисуется вообще. Unity добавляет
    /// его не во всех путях AddComponent, поэтому подстраховываемся здесь.
    /// </summary>
    protected override void Awake()
    {
        if (GetComponent<CanvasRenderer>() == null)
            gameObject.AddComponent<CanvasRenderer>();

        base.Awake();
    }

    [Tooltip("Размер фаски по краю, px (для кнопок 6–10, для панелей 10–16)")]
    [SerializeField, Range(0f, 64f)] private float _bevel = 12f;

    [Tooltip("Утопленная площадка внутри плиты, px. 0 — без углубления")]
    [SerializeField, Range(0f, 64f)] private float _recess;

    [Tooltip("Цвет утопленной площадки")]
    [SerializeField] private Color _recessColor = new Color(0.09f, 0.075f, 0.06f, 1f);

    [Tooltip("Шероховатость грани: разброс яркости по вершинам")]
    [SerializeField, Range(0f, 0.45f)] private float _grain = 0.1f;

    [Tooltip("Сила подсветки/затенения фаски")]
    [SerializeField, Range(0f, 0.6f)] private float _edgeShade = 0.3f;

    [Tooltip("Скол на правой грани (деталь для крупных плит)")]
    [SerializeField] private bool _chipped = true;

    /// <summary>Размер фаски, px.</summary>
    public float Bevel => _bevel;

    /// <summary>Глубина утопленной площадки, px.</summary>
    public float Recess => _recess;

    /// <summary>Настраивает плиту и перестраивает её сетку.</summary>
    public void Configure(Color plateColor, float bevel = 12f, float recess = 0f,
        float grain = 0.1f, bool chipped = true)
    {
        color = plateColor;
        _bevel = Mathf.Max(0f, bevel);
        _recess = Mathf.Max(0f, recess);
        _grain = Mathf.Clamp(grain, 0f, 0.45f);
        _chipped = chipped;
        SetVerticesDirty();
    }

    /// <summary>Цвет утопленной площадки (по умолчанию тёмный камень).</summary>
    public void SetRecessColor(Color value)
    {
        _recessColor = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = GetPixelAdjustedRect();

        // крупная плита получает скол на правой грани, мелкая — просто фаску
        bool chipped = _chipped && r.width > 140f && r.height > 90f;
        AddPlate(vh, r, _bevel, color, chipped, true);

        if (_recess > 1f)
        {
            float inset = Mathf.Min(_recess, Mathf.Min(r.width, r.height) * 0.4f);
            Rect inner = Rect.MinMaxRect(r.xMin + inset, r.yMin + inset, r.xMax - inset, r.yMax - inset);
            AddPlate(vh, inner, Mathf.Max(4f, _bevel * 0.6f), _recessColor, false, false);
        }
    }

    /// <summary>Одна плита: фаска (кольцо) + грань (веер к центру).</summary>
    private void AddPlate(VertexHelper vh, Rect r, float bevel, Color baseColor, bool chipped, bool raised)
    {
        float k = Mathf.Clamp(bevel, 2f, Mathf.Min(r.width, r.height) * 0.24f);
        Vector2[] outline = BuildOutline(r, k, chipped);
        int n = outline.Length;
        int start = vh.currentVertCount;
        Vector2 center = r.center;

        for (int ring = 0; ring < 2; ring++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i];
                Color c;

                if (ring == 0)
                {
                    // фаска: сверху подсвечена, снизу в тени (у углубления — наоборот)
                    bool top = p.y > center.y;
                    float shade = raised ? _edgeShade : -_edgeShade * 0.6f;
                    float f = 1f + (top ? shade : -shade);
                    c = new Color(baseColor.r * f, baseColor.g * f, baseColor.b * f, baseColor.a);
                }
                else
                {
                    p = Vector2.Lerp(p, center, 0.22f);
                    c = Grain(baseColor, p);
                }

                vh.AddVert(p, c, Vector2.zero);
            }
        }

        vh.AddVert(center, Grain(baseColor, center), Vector2.zero);

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            vh.AddTriangle(start + i, start + j + n, start + j);
            vh.AddTriangle(start + i, start + i + n, start + j + n);
            vh.AddTriangle(start + 2 * n, start + j + n, start + i + n);
        }
    }

    /// <summary>Контур плиты: фаска по углам и (необязательно) скол на правой грани.</summary>
    private static Vector2[] BuildOutline(Rect r, float k, bool chipped)
    {
        if (!chipped)
        {
            return new[]
            {
                new Vector2(r.xMin + k, r.yMin), new Vector2(r.xMax - k, r.yMin),
                new Vector2(r.xMax, r.yMin + k), new Vector2(r.xMax, r.yMax - k),
                new Vector2(r.xMax - k, r.yMax), new Vector2(r.xMin + k, r.yMax),
                new Vector2(r.xMin, r.yMax - k), new Vector2(r.xMin, r.yMin + k),
            };
        }

        // скол: маленькая выемка на правой грани по центру
        return new[]
        {
            new Vector2(r.xMin + k, r.yMin), new Vector2(r.xMax - k, r.yMin),
            new Vector2(r.xMax, r.yMin + k), new Vector2(r.xMax, r.center.y),
            new Vector2(r.xMax - k * 0.35f, r.center.y + k * 0.45f), new Vector2(r.xMax, r.center.y + k),
            new Vector2(r.xMax, r.yMax - k), new Vector2(r.xMax - k, r.yMax),
            new Vector2(r.xMin + k, r.yMax), new Vector2(r.xMin, r.yMax - k),
            new Vector2(r.xMin, r.yMin + k)
        };
    }

    /// <summary>Детерминированная шероховатость: одна и та же точка всегда одного оттенка.</summary>
    private Color Grain(Color c, Vector2 p)
    {
        if (_grain <= 0.001f)
            return c;

        float h = Mathf.Repeat(Mathf.Sin(Vector2.Dot(p, new Vector2(12.9898f, 78.233f))) * 43758.5453f, 1f);
        float f = 1f + (h - 0.5f) * _grain;
        return new Color(c.r * f, c.g * f, c.b * f, c.a);
    }
}
