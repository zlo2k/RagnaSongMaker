using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Рассчитывает мировые позиции 4-х барабанов на основе DrumLayoutConfig.
    /// Барабаны стоят вертикально: плоскость головки смотрит на игрока,
    /// DrumTiltAngle наклоняет её вверх (0 — строго вертикально).
    /// </summary>
    public class DrumPositioner : MonoBehaviour
    {
        [SerializeField] private DrumLayoutConfig _config;
        [SerializeField] private Transform _xrOrigin;

        /// <summary>Смещение головки вперёд от центра корпуса, метры (как в Drum.prefab).</summary>
        public const float FaceOffset = 0.08f;

        /// <summary>Вычисленные позиции (обновляются при вызове Calculate).</summary>
        public Vector3[] DrumPositions { get; private set; } = new Vector3[4];

        /// <summary>Поворот для всех барабанов.</summary>
        public Quaternion DrumRotation { get; private set; } = Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>Нормаль плоскости головки в мире — смотрит на игрока.</summary>
        public Vector3 FaceNormal => DrumRotation * Vector3.up;

        /// <summary>Центр плоскости головки для позиции корпуса <paramref name="drumPosition"/>.</summary>
        public Vector3 FaceCenter(Vector3 drumPosition) => drumPosition + FaceNormal * FaceOffset;

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
            Vector3 baseRight = _xrOrigin.right;
            Vector3 playerPos = _xrOrigin.position;

            // Игнорируем наклон головы — барабаны на полу
            baseForward.y = 0f;
            baseForward.Normalize();
            baseRight.y = 0f;
            baseRight.Normalize();

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

            // Вертикальная стойка: головка смотрит на игрока, DrumTiltAngle наклоняет её вверх.
            // Было Quaternion.Euler(tilt, 0, 0) — головка смотрела вверх, барабаны лежали.
            DrumRotation = Quaternion.Euler(-(90f - _config.DrumTiltAngle), 0f, 0f);
        }

        /// <summary>
        /// Рисует гизмы барабанов в редакторе: кольцо головки и нормаль к плоскости.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (_config == null || _xrOrigin == null) return;

            Calculate();

            Gizmos.color = Color.yellow;

            for (int i = 0; i < 4; i++)
            {
                Vector3 face = FaceCenter(DrumPositions[i]);
                float radius = _config.DrumDiameter * 0.5f;

                Gizmos.DrawWireSphere(face, radius);
                Gizmos.DrawRay(face, FaceNormal * 0.3f);
            }
        }
    }
}
