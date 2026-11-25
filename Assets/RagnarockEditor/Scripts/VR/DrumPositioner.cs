using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Рассчитывает мировые позиции 4-х барабанов на основе DrumLayoutConfig.
    /// </summary>
    public class DrumPositioner : MonoBehaviour
    {
        [SerializeField] private DrumLayoutConfig _config;
        [SerializeField] private Transform _xrOrigin;

        /// <summary>Вычисленные позиции (обновляются при вызове Calculate).</summary>
        public Vector3[] DrumPositions { get; private set; } = new Vector3[4];

        /// <summary>Поворот для всех барабанов.</summary>
        public Quaternion DrumRotation { get; private set; }

        /// <summary>
        /// Конфиг расстановки. Позволяет калибровке подставить тот же DrumLayoutConfig,
        /// что она обновляет, если поле в инспекторе не назначено.
        /// </summary>
        public DrumLayoutConfig Config
        {
            get => _config;
            set => _config = value;
        }

        /// <summary>XR Origin (объект, относительно которого считается расстановка).</summary>
        public Transform XROrigin
        {
            get => _xrOrigin;
            set => _xrOrigin = value;
        }

        /// <summary>Готов ли позиционер к расчёту (есть конфиг и XR Origin).</summary>
        public bool IsReady => _config != null && _xrOrigin != null;

        private void Awake()
        {
            if (_xrOrigin == null)
            {
                // Пытаемся найти XR Origin автоматически
                var xrOriginGO = GameObject.Find("XR Origin");
                if (xrOriginGO == null)
                {
                    var camera = Camera.main;
                    if (camera != null) _xrOrigin = camera.transform;
                }
                else
                {
                    _xrOrigin = xrOriginGO.transform;
                }
            }
        }

        /// <summary>
        /// Вычисляет позиции 4-х барабанов относительно XR Origin.
        /// </summary>
        public void Calculate()
        {
            if (_config == null)
            {
                Debug.LogError("[DrumPositioner] DrumLayoutConfig не назначен!");
                return;
            }

            if (_xrOrigin == null)
            {
                Debug.LogError("[DrumPositioner] XR Origin не найден!");
                return;
            }

            Vector3 baseForward = _xrOrigin.forward;

            Vector3 playerPos = _xrOrigin.position;

            // Игнорируем наклон головы — барабаны на полу
            baseForward.y = 0f;
            baseForward.Normalize();
            if (baseForward.sqrMagnitude < 0.001f) baseForward = Vector3.forward;
            Vector3 baseRight = Vector3.Cross(Vector3.up, baseForward);

            float halfWidth = _config.TotalWidth * 0.5f;
            float spacing = _config.TotalWidth / 3f;

            for (int i = 0; i < 4; i++)
            {
                float xOffset = -halfWidth + i * spacing;
                Vector3 pos = playerPos
                    + baseForward * _config.DistanceFromPlayer
                    + baseRight * xOffset;

                // Высота от пола
                pos.y = _config.DrumHeight;

                // Применяем дугу, если задана
                if (_config.ArcRadius > 0.001f)
                {
                    float t = (i - 1.5f) / 1.5f; // [-1, +1]
                    float angle = t * Mathf.PI * 0.25f; // ±45°
                    float arcX = Mathf.Sin(angle) * _config.ArcRadius;
                    float arcZ = Mathf.Cos(angle) * _config.ArcRadius;
                    pos += baseRight * arcX + baseForward * (arcZ - _config.ArcRadius);
                }

                DrumPositions[i] = pos;
            }

            // Поворот: наклон к игроку
            DrumRotation = Quaternion.LookRotation(baseForward, Vector3.up) * Quaternion.Euler(-_config.DrumTiltAngle, 0f, 0f);
        }

        /// <summary>
        /// Рисует гизмы барабанов в редакторе.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (_config == null || _xrOrigin == null) return;

            Calculate();

            Gizmos.color = Color.yellow;
            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawWireSphere(DrumPositions[i], _config.DrumDiameter * 0.5f);
                Gizmos.DrawRay(DrumPositions[i], DrumRotation * Vector3.up * 0.3f);
            }
        }
    }
}
