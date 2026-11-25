using System.Collections.Generic;
using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.Recording
{
    /// <summary>
    /// Буфер сырых ударов. Принимает удары от DrumManager и сохраняет в RecordSession.
    ///
    /// Если в сцене есть RecordManager — он является единственным приёмником ударов
    /// и вызывает <see cref="AddHit"/> сам (исключает двойной учёт).
    /// Если RecordManager нет — NoteCollector подписывается на DrumManager напрямую.
    /// </summary>
    public class NoteCollector : MonoBehaviour
    {
        [SerializeField] private RecordManager _recordManager;
        [SerializeField] private VR.DrumManager _drumManager;

        /// <summary>Минимальный интервал между ударами по одному барабану (debounce).</summary>
        [SerializeField] private float _drumDebounceTime = 0.08f;

        private RecordSession _session;
        private bool _selfSubscribed;
        private float[] _lastHitTimes = new float[4] { -1f, -1f, -1f, -1f };

        /// <summary>Сырые удары, записанные в текущем сеансе.</summary>
        public int HitCount => _session != null ? _session.TotalRawHits : 0;

        private void Awake()
        {
            _session = RecordSession.Instance;
        }

        private void OnEnable()
        {
            if (_recordManager == null)
                _recordManager = FindFirstObjectByType<RecordManager>();

            if (_drumManager == null)
                _drumManager = FindFirstObjectByType<VR.DrumManager>();

            if (_recordManager != null)
            {
                // RecordManager сам подписан на DrumManager.OnAnyDrumHit и вызовет AddHit.
                Debug.Log("[NoteCollector] Удары принимаются через RecordManager.");
                return;
            }

            // Обратная совместимость: самостоятельная подписка без RecordManager.
            if (_drumManager != null)
            {
                _drumManager.OnAnyDrumHit += OnDrumHit;
                _selfSubscribed = true;
            }
        }

        private void OnDisable()
        {
            if (_selfSubscribed && _drumManager != null)
                _drumManager.OnAnyDrumHit -= OnDrumHit;

            _selfSubscribed = false;
        }

        /// <summary>
        /// Обработчик удара по барабану (режим без RecordManager).
        /// </summary>
        private void OnDrumHit(int drumIndex, float timeSeconds, float velocity)
        {
            // Если RecordManager есть, он уже вызвал AddHit — второй раз не считаем.
            if (_recordManager != null)
                return;

            AddHit(drumIndex, timeSeconds);
        }

        /// <summary>
        /// Добавляет удар в сессию. Вызывается RecordManager'ом и напрямую (legacy).
        /// </summary>
        /// <param name="drumIndex">Индекс барабана (0-3).</param>
        /// <param name="timeSeconds">Время удара от начала трека.</param>
        public void AddHit(int drumIndex, float timeSeconds)
        {
            if (_session == null)
                _session = RecordSession.Instance;

            if (_session == null)
                return;

            if (_recordManager != null && _recordManager.CurrentState != RecordManager.State.Recording)
                return;

            int index = Mathf.Clamp(drumIndex, 0, _lastHitTimes.Length - 1);
            float time = Mathf.Max(0f, timeSeconds);

            // Debounce по каждому барабану отдельно.
            if (time - _lastHitTimes[index] < _drumDebounceTime)
                return;

            _lastHitTimes[index] = time;
            _session.AddRawHit(time, index);

            Debug.Log($"[NoteCollector] Удар зафиксирован: барабан={index}, t={time:F3}с");
        }

        /// <summary>
        /// Удаляет удары, записанные позже указанного времени (перемотка назад).
        /// </summary>
        public void RemoveAfter(float timeSeconds)
        {
            if (_session == null)
                _session = RecordSession.Instance;

            if (_session != null)
                _session.RemoveHitsAfter(timeSeconds);

            // Сбрасываем debounce, чтобы удары после перемотки не отбрасывались.
            for (int i = 0; i < _lastHitTimes.Length; i++)
            {
                if (_lastHitTimes[i] > timeSeconds)
                    _lastHitTimes[i] = -1f;
            }
        }

        /// <summary>Сбрасывает состояние debounce (начало нового дубля).</summary>
        public void ResetDebounce()
        {
            for (int i = 0; i < _lastHitTimes.Length; i++)
                _lastHitTimes[i] = -1f;
        }

        /// <summary>
        /// Возвращает сырые удары за указанный промежуток времени.
        /// </summary>
        public List<RawHit> GetHitsInRange(float startTime, float endTime)
        {
            if (_session == null)
                return new List<RawHit>();

            return _session.RawHits.FindAll(h =>
                h.TimeSeconds >= startTime && h.TimeSeconds <= endTime);
        }

        /// <summary>
        /// Возвращает статистику по барабанам.
        /// </summary>
        public int[] GetHitCounts()
        {
            if (_session == null)
                return new int[4];

            return _session.GetHitsPerDrum();
        }
    }
}
