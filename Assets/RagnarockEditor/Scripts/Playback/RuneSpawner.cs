using System;
using System.Collections.Generic;
using UnityEngine;
using RagnarockEditor.Data;

namespace RagnarockEditor.Playback
{
    /// <summary>
    /// Спавнит руны для предпросмотра уровня и ведёт их по прямой к барабанам.
    /// Используется пул объектов: Instantiate выполняется только при создании/росте пула,
    /// не в кадре спавна. Движение считается от времени песни, которое передаёт
    /// <see cref="PlaybackController"/>, поэтому пауза и скорость не рассинхронизируют руны.
    /// </summary>
    public class RuneSpawner : MonoBehaviour
    {
        [Header("Префаб руны")]
        [SerializeField] private GameObject _runePrefab;

        [Header("Компоненты сцены")]
        [Tooltip("Менеджер барабанов. Если пусто — ищется в сцене.")]
        [SerializeField] private VR.DrumManager _drumManager;
        [Tooltip("Позиционер барабанов (запасной источник позиций). Если пусто — ищется в сцене.")]
        [SerializeField] private VR.DrumPositioner _drumPositioner;

        [Header("Настройки спавна")]
        [Tooltip("NoteJumpTime: время подлёта руны к барабану в секундах песни. При скорости 0.5x полёт занимает вдвое больше реального времени.")]
        [SerializeField] private float _spawnAdvanceTime = 1.5f;

        [Tooltip("Базовый размер пула рун (расширяется автоматически под плотность нот).")]
        [SerializeField] private int _poolSize = 32;

        [Tooltip("Дистанция, с которой руна вылетает в сторону игрока (по направлению от игрока), метры.")]
        [SerializeField] private float _approachDistance = 2.2f;


        [Tooltip("Длительность подсветки барабана при попадании, секунды.")]
        [SerializeField] private float _drumHighlightDuration = 0.2f;

        [Tooltip("Точка отсчёта запасных позиций барабанов, если DrumManager/DrumPositioner недоступны.")]
        [SerializeField] private Transform _fallbackOrigin;

        private const int MinPoolSize = 1;
        private const int MaxPoolSize = 128;

        private const float FallbackDistance = 0.8f;
        private const float FallbackHeight = 0.9f;
        private const float FallbackWidth = 1.08f;

        /// <summary>Руна достигла барабана: (noteIndex, drumIndex).</summary>
        public event Action<int, int> OnRuneHit;

        private sealed class ActiveRune
        {
            public RuneVisual Visual;
            public int NoteIndex;
            public int DrumIndex;
            public float NoteTime;
            public float SpawnTime;
            public bool Impacted;
        }

        private struct PendingHighlight
        {
            public int DrumIndex;
            public float Remaining;
        }

        private Queue<RuneVisual> _pool;
        private List<ActiveRune> _active;
        private Stack<ActiveRune> _entryCache;
        private List<PendingHighlight> _pendingHighlights;

        private float _bpm = 120f;
        private float _songDuration;
        private int _noteCount;
        private int _createdCount;
        private int _requiredPoolSize;
        private bool _prefabWarned;
        private bool _poolGrowWarned;

        /// <summary>NoteJumpTime: время подлёта руны к барабану, секунды песни.</summary>
        public float NoteJumpTime
        {
            get => _spawnAdvanceTime;
            set => _spawnAdvanceTime = Mathf.Clamp(value, 0.1f, 10f);
        }

        /// <summary>Совместимость со старым API (то же, что NoteJumpTime).</summary>
        public float SpawnAdvanceTime => _spawnAdvanceTime;

        /// <summary>Сколько рун сейчас в полёте/на барабанах.</summary>
        public int ActiveRuneCount => _active != null ? _active.Count : 0;

        /// <summary>BPM, с которым руны переводятся из битов в секунды.</summary>
        public float BPM => _bpm;

        /// <summary>Длительность песни, переданная при инициализации (0 — неизвестна).</summary>
        public float SongDuration => _songDuration;

        /// <summary>Количество нот в последней подготовке.</summary>
        public int NoteCount => _noteCount;

        // -------------------------------------------------------- Unity-цикл

        private void Awake()
        {
            EnsureCollections();
            ResolveReferences();
            EnsurePool(_poolSize);
        }

        /// <summary>
        /// Коллекции создаются лениво: публичный метод могут вызвать раньше Awake.
        /// </summary>
        private void EnsureCollections()
        {
            if (_pool == null) _pool = new Queue<RuneVisual>();
            if (_active == null) _active = new List<ActiveRune>();
            if (_entryCache == null) _entryCache = new Stack<ActiveRune>();
            if (_pendingHighlights == null) _pendingHighlights = new List<PendingHighlight>();
        }

        // ------------------------------------------------------ Публичный API

        /// <summary>
        /// Инициализирует спавнер: BPM, позиции барабанов и размер пула.
        /// </summary>
        public void Initialize(List<Note> notes, float bpm)
        {
            Initialize(notes, bpm, 0f);
        }

        /// <summary>
        /// Инициализирует спавнер: BPM, длительность песни, позиции барабанов и размер пула.
        /// </summary>
        public void Initialize(List<Note> notes, float bpm, float songDurationSeconds)
        {
            EnsureCollections();

            _bpm = bpm > 0.01f ? bpm : 120f;
            _songDuration = Mathf.Max(0f, songDurationSeconds);
            _noteCount = notes != null ? notes.Count : 0;

            ClearAllRunes();
            ResolveReferences();

            int peak = EstimatePeakConcurrency(notes);

            // Запас на хвост плавного исчезновения (0.4 с) после попадания
            int required = Mathf.CeilToInt(peak * 1.5f) + 2;
            _requiredPoolSize = Mathf.Clamp(Mathf.Max(_poolSize, required), MinPoolSize, MaxPoolSize);
            EnsurePool(_requiredPoolSize);

            Debug.Log($"[RuneSpawner] Готов: нот={_noteCount}, BPM={_bpm:0.#}, " +
                      $"время подлёта={_spawnAdvanceTime:0.##}с, пул={_requiredPoolSize}");
        }

        /// <summary>
        /// Спавнит одну руну. Позиция барабана берётся у DrumManager/DrumPositioner.
        /// </summary>
        public void SpawnRune(Note note, int noteIndex)
        {
            if (note == null)
                return;

            EnsureCollections();

            RuneVisual visual = GetFromPool();
            if (visual == null)
                return;

            int drumIndex = Mathf.Clamp(note.LineIndex, 0, 3);
            float noteTime = note.GetTimeInSeconds(_bpm);
            float jumpTime = Mathf.Max(0.05f, _spawnAdvanceTime);

            // Как в игре: руна летит по палубе в плоскости над барабанами —
            // от дальней точки в сторону игрока, а не падает сверху.
            var drum = _drumManager != null ? _drumManager.Drums[drumIndex] : null;
            Quaternion lane = drum != null ? drum.transform.rotation :
                Quaternion.LookRotation(DeckOrientation.Forward, Vector3.up) * Quaternion.Euler(-8f, 0f, 0f);
            Vector3 target = drum != null ? drum.RuneCenter :
                GetDrumPosition(drumIndex) + lane * Vector3.up * 0.1f;
            Vector3 start = target + lane * Vector3.forward * Mathf.Max(0.5f, _approachDistance);

            ActiveRune entry = GetEntry();
            entry.Visual = visual;
            entry.NoteIndex = noteIndex;
            entry.DrumIndex = drumIndex;
            entry.NoteTime = noteTime;
            entry.SpawnTime = noteTime - jumpTime;
            entry.Impacted = false;

            visual.Activate(start, target, drumIndex, noteIndex, lane);
            _active.Add(entry);
        }

        /// <summary>
        /// Обновляет все активные руны: полёт, попадание, вспышка и возврат в пул.
        /// </summary>
        /// <param name="songTime">Текущее время песни в секундах.</param>
        /// <param name="deltaTime">Реальное время кадра (для вспышки и подсветки).</param>
        public void UpdateRunes(float songTime, float deltaTime)
        {
            EnsureCollections();

            float jumpTime = Mathf.Max(0.05f, _spawnAdvanceTime);
            float dt = deltaTime > 0f ? deltaTime : 0f;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveRune entry = _active[i];
                RuneVisual visual = entry.Visual;

                if (visual == null)
                {
                    ReleaseEntry(i);
                    continue;
                }

                if (!entry.Impacted)
                {
                    float progress = (songTime - entry.SpawnTime) / jumpTime;

                    if (progress >= 1f)
                    {
                        entry.Impacted = true;
                        visual.SetProgress(1f);
                        visual.PlayImpact();
                        HighlightDrum(entry.DrumIndex);
                        OnRuneHit?.Invoke(entry.NoteIndex, entry.DrumIndex);
                    }
                    else
                    {
                        visual.SetProgress(progress > 0f ? progress : 0f);
                    }
                }

                visual.Tick(dt);

                if (entry.Impacted && !visual.IsImpactPlaying)
                    ReturnEntryToPool(i);
            }

            TickHighlights(dt);
        }

        /// <summary>
        /// Возвращает конкретную руну в пул (совместимость со старым API).
        /// </summary>
        public void ReturnToPool(RuneVisual rune)
        {
            if (rune == null)
                return;

            EnsureCollections();

            int index = _active.FindIndex(e => e.Visual == rune);
            if (index >= 0)
            {
                ReturnEntryToPool(index);
                return;
            }

            rune.Deactivate();
            rune.gameObject.SetActive(false);
            _pool.Enqueue(rune);
        }

        /// <summary>
        /// Убирает все активные руны и подсветку с барабанов.
        /// </summary>
        public void ClearAllRunes()
        {
            EnsureCollections();

            for (int i = _active.Count - 1; i >= 0; i--)
                ReturnEntryToPool(i);

            ClearHighlights();
        }

        /// <summary>Мировая позиция барабана: DrumController, затем DrumPositioner, затем запасная схема.</summary>
        public Vector3 GetDrumPosition(int drumIndex)
        {
            int index = Mathf.Clamp(drumIndex, 0, 3);

            if (_drumManager != null && _drumManager.Drums != null &&
                index < _drumManager.Drums.Length && _drumManager.Drums[index] != null)
            {
                return _drumManager.Drums[index].transform.position;
            }

            if (_drumPositioner != null && _drumPositioner.DrumPositions != null &&
                index < _drumPositioner.DrumPositions.Length && !IsDegenerate(_drumPositioner.DrumPositions))
            {
                return _drumPositioner.DrumPositions[index];
            }

            return GetFallbackDrumPosition(index);
        }

        // ---------------------------------------------------------- Внутреннее

        private void ResolveReferences()        {
            if (_drumManager == null)
                _drumManager = FindAnyObjectByType<VR.DrumManager>();

            if (_drumPositioner == null)
                _drumPositioner = FindAnyObjectByType<VR.DrumPositioner>();
        }

        private void EnsurePool(int target)
        {
            if (_runePrefab == null)
            {
                if (!_prefabWarned)
                {
                    _prefabWarned = true;
                    Debug.LogError("[RuneSpawner] Не назначен префаб руны — предпросмотр будет без рун.");
                }

                return;
            }

            int desired = Mathf.Clamp(target, MinPoolSize, MaxPoolSize);

            while (_createdCount < desired)
            {
                RuneVisual rune = CreateRune();
                if (rune == null)
                    break;

                _createdCount++;
                _pool.Enqueue(rune);
            }
        }

        private RuneVisual CreateRune()
        {
            if (_runePrefab == null)
                return null;

            GameObject instance = Instantiate(_runePrefab, transform);
            instance.name = $"Rune_{_createdCount}";

            RuneVisual visual = instance.GetComponent<RuneVisual>();
            if (visual == null)
                visual = instance.AddComponent<RuneVisual>();

            instance.SetActive(false);
            return visual;
        }

        private RuneVisual GetFromPool()
        {
            EnsurePool(_poolSize);

            while (_pool.Count > 0)
            {
                RuneVisual visual = _pool.Dequeue();
                if (visual == null)
                    continue;

                visual.gameObject.SetActive(true);
                return visual;
            }

            // Оценка плотности не сошлась — расширяем пул (не чаще одного предупреждения).
            if (_createdCount < MaxPoolSize)
            {
                RuneVisual created = CreateRune();
                if (created != null)
                {
                    _createdCount++;
                    created.gameObject.SetActive(true);

                    if (!_poolGrowWarned)
                    {
                        _poolGrowWarned = true;
                        Debug.LogWarning("[RuneSpawner] Пул расширен под фактическую плотность нот.");
                    }

                    return created;
                }
            }

            return null;
        }

        private ActiveRune GetEntry()
        {
            if (_entryCache != null && _entryCache.Count > 0)
                return _entryCache.Pop();

            return new ActiveRune();
        }

        private void ReturnEntryToPool(int index)
        {
            if (_active == null || index < 0 || index >= _active.Count)
                return;

            ActiveRune entry = _active[index];
            _active.RemoveAt(index);

            if (entry.Visual != null)
            {
                entry.Visual.Deactivate();
                entry.Visual.gameObject.SetActive(false);
                _pool.Enqueue(entry.Visual);
            }

            entry.Visual = null;
            entry.Impacted = false;
            _entryCache.Push(entry);
        }

        private void ReleaseEntry(int index)
        {
            if (_active == null || index < 0 || index >= _active.Count)
                return;

            ActiveRune entry = _active[index];
            _active.RemoveAt(index);

            entry.Visual = null;
            entry.Impacted = false;
            _entryCache.Push(entry);
        }

        private void HighlightDrum(int drumIndex)
        {
            if (_drumManager == null)
                return;

            _drumManager.HighlightDrum(drumIndex, true);

            if (_drumHighlightDuration <= 0f)
            {
                _drumManager.HighlightDrum(drumIndex, false);
                return;
            }

            _pendingHighlights.Add(new PendingHighlight
            {
                DrumIndex = drumIndex,
                Remaining = _drumHighlightDuration
            });
        }

        private void TickHighlights(float deltaTime)
        {
            if (_pendingHighlights == null || _pendingHighlights.Count == 0)
                return;

            for (int i = _pendingHighlights.Count - 1; i >= 0; i--)
            {
                PendingHighlight slot = _pendingHighlights[i];
                slot.Remaining -= deltaTime;

                if (slot.Remaining <= 0f)
                {
                    _pendingHighlights.RemoveAt(i);

                    if (_drumManager != null)
                        _drumManager.HighlightDrum(slot.DrumIndex, false);
                }
                else
                {
                    _pendingHighlights[i] = slot;
                }
            }
        }

        private void ClearHighlights()
        {
            if (_pendingHighlights == null || _pendingHighlights.Count == 0)
                return;

            for (int i = 0; i < _pendingHighlights.Count; i++)
            {
                if (_drumManager != null)
                    _drumManager.HighlightDrum(_pendingHighlights[i].DrumIndex, false);
            }

            _pendingHighlights.Clear();
        }

        private int EstimatePeakConcurrency(List<Note> notes)
        {
            if (notes == null || notes.Count == 0)
                return 0;

            float window = Mathf.Max(0.05f, _spawnAdvanceTime);
            int peak = 0;
            int start = 0;

            for (int i = 0; i < notes.Count; i++)
            {
                Note current = notes[i];
                if (current == null)
                    continue;

                float currentTime = current.GetTimeInSeconds(_bpm);

                while (start < i)
                {
                    Note first = notes[start];
                    float firstTime = first != null ? first.GetTimeInSeconds(_bpm) : currentTime;

                    if (currentTime - firstTime <= window)
                        break;

                    start++;
                }

                int count = i - start + 1;
                if (count > peak)
                    peak = count;
            }

            return peak;
        }

        private static bool IsDegenerate(Vector3[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].sqrMagnitude > 0.0001f)
                    return false;
            }

            return true;
        }

        private Vector3 GetFallbackDrumPosition(int drumIndex)
        {
            Transform origin = _fallbackOrigin;

            if (origin == null && Camera.main != null)
                origin = Camera.main.transform;

            if (origin == null)
                origin = transform;

            Vector3 forward = origin.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;

            right.Normalize();

            Vector3 center = origin.position + forward * FallbackDistance;
            center.y = FallbackHeight;

            float spacing = FallbackWidth / 3f;
            return center + right * ((drumIndex - 1.5f) * spacing);
        }
    }
}
