using UnityEngine;
using UnityEngine.Rendering;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Визуал одной руны. Компонент пассивный: позицию и время жизни задаёт
    /// <see cref="RuneSpawner"/>, а здесь только применяются позиция, цвет и прозрачность.
    /// Размер постоянный: появление и исчезновение — это alpha-фейд на границах полёта.
    /// </summary>
    public class RuneVisual : MonoBehaviour
    {
        [Header("Материалы")]
        [Tooltip("Рендерер руны. Если пусто — берётся MeshRenderer на этом объекте.")]
        [SerializeField] private MeshRenderer _renderer;

        [Tooltip("Материалы по индексу барабана (0-3). Если пусто — материал префаба тонируется цветом.")]
        [SerializeField] private Material[] _drumMaterials;

        [Tooltip("Цвета рун по индексу барабана (используются, если _drumMaterials пуст).")]
        [SerializeField] private Color[] _drumColors =
        {
            new Color(0.95f, 0.25f, 0.28f, 1f),
            new Color(0.25f, 0.55f, 1.00f, 1f),
            new Color(0.30f, 0.90f, 0.45f, 1f),
            new Color(1.00f, 0.80f, 0.20f, 1f)
        };

        [Header("Анимация")]
        [Tooltip("Скорость вращения руны, градусов в секунду (0 — без вращения).")]
        [SerializeField] private float _spinSpeed = 90f;

        [Tooltip("Плавное появление руны на границе появления, секунды.")]
        [SerializeField] private float _fadeInDuration = 1f;

        [Tooltip("Длительность плавного исчезновения при попадании, секунды.")]
        [SerializeField] private float _impactDuration = 1f;

        private Vector3 _startPosition;
        private Vector3 _targetPosition;
        private Vector3 _baseScale = Vector3.one;
        private Color _currentColor = Color.white;
        private float _alpha = 1f;
        private float _fadeInTimer;
        private bool _hasBaseScale;
        private bool _isActive;
        private int _drumIndex = -1;
        private int _noteIndex = -1;
        private Quaternion _laneRotation;
        private float _spinAngle;
        private float _impactTimer;
        private float _impactStartAlpha;

        /// <summary>Руна сейчас в полёте или на барабане.</summary>
        public bool IsActive => _isActive;

        /// <summary>Индекс барабана, к которому летит руна (-1 — не задан).</summary>
        public int DrumIndex => _drumIndex;

        /// <summary>Индекс ноты в подготовленном списке.</summary>
        public int NoteIndex => _noteIndex;

        /// <summary>Проигрывается ли сейчас вспышка попадания.</summary>
        public bool IsImpactPlaying => _impactTimer > 0f;

        private Vector3 BaseScale
        {
            get
            {
                if (!_hasBaseScale)
                    CaptureBaseScale();

                return _baseScale;
            }
        }

        private void Awake()
        {
            ResolveRenderer();
            BuildDiscVisual();
            CaptureBaseScale();
        }

        /// <summary>
        /// Превращает сферу из префаба в плоский кружок фиксированного мирового размера.
        /// Размер задаётся в метрах и не зависит от масштаба префаба/родителя.
        /// </summary>
        private void BuildDiscVisual()
        {
            if (_renderer == null)
                _renderer = GetComponent<MeshRenderer>();

            if (_renderer == null)
                return;

            var filter = GetComponent<MeshFilter>();
            if (filter == null)
                filter = gameObject.AddComponent<MeshFilter>();

            filter.sharedMesh = DiscVisual.Mesh;
            _renderer.sharedMaterial = DiscVisual.Material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            var collider = GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            float parentScale = 1f;
            if (transform.parent != null)
            {
                Vector3 parentLossy = transform.parent.lossyScale;
                parentScale = Mathf.Max(Mathf.Abs(parentLossy.x), Mathf.Abs(parentLossy.z));
            }

            transform.localScale = DiscVisual.ScaleForWorldSize(
                DiscVisual.Radius, DiscVisual.Thickness, parentScale);
        }

        /// <summary>
        /// Активирует руну в начальной точке и красит её под барабан.
        /// </summary>
        public void Activate(Vector3 startPosition, Vector3 targetPosition, int drumIndex, int noteIndex)
        {
            Activate(startPosition, targetPosition, drumIndex, noteIndex, Quaternion.identity);
        }

        public void Activate(Vector3 startPosition, Vector3 targetPosition, int drumIndex, int noteIndex, Quaternion laneRotation)
        {
            ResolveRenderer();
            _laneRotation = laneRotation;
            _startPosition = startPosition;
            _targetPosition = targetPosition;

            _drumIndex = drumIndex;
            _noteIndex = noteIndex;
            _spinAngle = 0f;
            _impactTimer = 0f;
            _isActive = true;

            // появляемся плавно на границе появления, размер не меняем
            _alpha = 0f;
            _fadeInTimer = Mathf.Max(0.01f, _fadeInDuration);

            transform.position = startPosition;
            transform.rotation = _laneRotation;
            transform.localScale = BaseScale;

            ApplyDrumColor(drumIndex >= 0 ? drumIndex : noteIndex);
        }

        /// <summary>
        /// Деактивирует руну (возврат в пул выполняет RuneSpawner).
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            _impactTimer = 0f;
            transform.localScale = BaseScale;
        }

        /// <summary>
        /// Ставит руну на долю пути 0..1 к барабану (прямая линия).
        /// </summary>
        public void SetProgress(float progress)
        {
            if (!_isActive)
                return;

            transform.position = Vector3.Lerp(_startPosition, _targetPosition, Mathf.Clamp01(progress));
        }

        /// <summary>
        /// Начало исчезновения при попадании: диск не меняет размер, а плавно гаснет.
        /// </summary>
        public void PlayImpact()
        {
            _impactStartAlpha = _alpha;
            _impactTimer = Mathf.Max(.05f, _impactDuration);
            _fadeInTimer = 0f;
        }

        /// <summary>
        /// Вращение и alpha-фейд. Вызывает RuneSpawner раз в кадр.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isActive)
                return;

            float dt = deltaTime > 0f ? deltaTime : 0f;

            if (_spinSpeed != 0f && dt > 0f)
            {
                _spinAngle += _spinSpeed * dt;
                if (_spinAngle > 360f)
                    _spinAngle -= 360f;
                transform.rotation = _laneRotation * Quaternion.Euler(0f, _spinAngle, 0f);
            }

            // плавное появление на границе
            if (_fadeInTimer > 0f)
            {
                _fadeInTimer -= dt;
                _alpha = Mathf.Clamp01(1f - _fadeInTimer / Mathf.Max(0.01f, _fadeInDuration));
                ApplyAlpha();
            }

            // плавное исчезновение над барабаном
            if (_impactTimer > 0f)
            {
                _impactTimer -= dt;

                if (_impactTimer <= 0f)
                {
                    _impactTimer = 0f;
                    _alpha = 0f;
                }
                else
                {
                    _alpha = _impactStartAlpha * Mathf.SmoothStep(0f, 1f, _impactTimer / Mathf.Max(.05f, _impactDuration));
                }

                ApplyAlpha();
            }
        }

        /// <summary>
        /// Задаёт цвет руны (прозрачность применяется отдельно, при фейде).
        /// </summary>
        public void SetColor(Color color)
        {
            _currentColor = color;
            ApplyAlpha();
        }

        /// <summary>Применяет текущий цвет и прозрачность через MaterialPropertyBlock.</summary>
        private void ApplyAlpha()
        {
            ResolveRenderer();
            if (_renderer == null)
                return;

            DiscVisual.ApplyColor(_renderer, _currentColor, _alpha);
        }

        /// <summary>
        /// Задаёт масштаб руны относительно базового (1 — базовый).
        /// </summary>
        public void SetScale(float scale)
        {
            if (scale <= 0f)
                return;

            transform.localScale = BaseScale * scale;
        }

        /// <summary>
        /// Красит руну под барабан: материалы из _drumMaterials либо цвет из _drumColors.
        /// </summary>
        public void ApplyDrumColor(int drumIndex)
        {
            ResolveRenderer();
            if (_renderer == null)
                return;

            if (_drumMaterials != null && _drumMaterials.Length > 0)
            {
                Material material = _drumMaterials[WrapIndex(drumIndex, _drumMaterials.Length)];
                if (material != null)
                {
                    SetColor(material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color);
                }

                return;
            }

            if (_drumColors != null && _drumColors.Length > 0)
                SetColor(_drumColors[WrapIndex(drumIndex, _drumColors.Length)]);
        }

        /// <summary>
        /// Совместимость со старым API: руна сразу ставится в начальную точку.
        /// Движение теперь выполняет RuneSpawner через <see cref="SetProgress"/>.
        /// </summary>
        public void Initialize(Vector3 startPosition, Vector3 targetPosition, float travelTime, int noteIndex)
        {
            Activate(startPosition, targetPosition, -1, noteIndex);
        }

        private void ResolveRenderer()
        {
            if (_renderer == null)
                _renderer = GetComponent<MeshRenderer>();
        }

        private void CaptureBaseScale()
        {
            Vector3 scale = transform.localScale;
            if (scale.sqrMagnitude < 1e-8f)
                scale = Vector3.one;

            _baseScale = scale;
            _hasBaseScale = true;
        }

        private static int WrapIndex(int index, int length)
        {
            if (length <= 0)
                return 0;

            int wrapped = index % length;
            return wrapped < 0 ? wrapped + length : wrapped;
        }
    }
}
