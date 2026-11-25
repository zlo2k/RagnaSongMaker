using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using RagnarockEditor.Data;
using RagnarockEditor.Localization;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Двухэтапная калибровка барабанов под игрока (M2, п.3.3 ТЗ):
    /// 1. Авто-рост: высота глаз из главной камеры за 2 секунды с прогрессом,
    ///    рост = headY + 0.12, высота барабанов = рост * 0.55.
    /// 2. Досягаемость: ждём нажатия триггера правого контроллера
    ///    (XRHandPoseDriver.IsTriggerPressed), меряем расстояние камера → контроллер,
    ///    DistanceFromPlayer = distance - DrumDiameter * 0.5, рамки [0.35..1.2].
    ///
    /// UI-поля необязательны: без них калибровка полностью работает «в лог»
    /// и через публичные события/методы.
    /// </summary>
    public class DrumCalibrationManager : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private Transform _xrOrigin;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private Transform _rightControllerTransform;
        [SerializeField] private DrumLayoutConfig _drumConfig;
        [SerializeField] private DrumPositioner _drumPositioner;
        [SerializeField] private DrumManager _drumManager;

        [Tooltip("Драйвер правой руки (XRHandPoseDriver). Если пусто — ищется в сцене.")]
        [SerializeField] private XRHandPoseDriver _rightHandPoseDriver;

        [Header("UI калибровки")]
        [SerializeField] private Canvas _calibrationCanvas;
        [SerializeField] private Text _instructionText;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _skipButton;
        [SerializeField] private Button _saveButton;
        [Tooltip("Необязательно: заполняемая полоска прогресса (Image → Filled)")]
        [SerializeField] private Image _progressFill;
        [Tooltip("Необязательно: текст прогресса/процентов")]
        [SerializeField] private Text _progressText;

        [Header("Параметры калибровки")]
        [Tooltip("Длительность замера роста (сек)")]
        [SerializeField] private float _heightMeasureSeconds = 2f;
        [Tooltip("Сколько ждать нажатия триггера на этапе досягаемости (сек)")]
        [SerializeField] private float _reachTimeoutSeconds = 30f;
        [Tooltip("Минимальное разумное расстояние до барабанов (м)")]
        [SerializeField] private float _minReach = 0.35f;
        [Tooltip("Максимальное разумное расстояние до барабанов (м)")]
        [SerializeField] private float _maxReach = 1.2f;
        [Tooltip("Запускать калибровку автоматически, если сохранённого профиля нет")]
        [SerializeField] private bool _autoStartIfNoProfile = false;

        public enum CalibrationStage
        {
            Idle,
            MeasuringHeight,
            MeasuringReach,
            Complete
        }

        /// <summary>Сменился этап калибровки (строка = имя CalibrationStage).</summary>
        public event Action<string> OnStageChanged;

        /// <summary>Прогресс текущего этапа, 0..1.</summary>
        public event Action<float> OnProgressChanged;

        /// <summary>Калибровка закончилась. applied=true — значения применены к сцене.</summary>
        public event Action<bool> OnFinished;

        /// <summary>Текущий этап калибровки.</summary>
        public CalibrationStage CurrentStage { get; private set; } = CalibrationStage.Idle;

        /// <summary>Измеренный рост игрока (м); 0 — ещё не измерялся.</summary>
        public float MeasuredHeight { get; private set; }

        /// <summary>Итоговое расстояние до барабанов (м) = измеренное − радиус барабана, в рамках [0.35..1.2].</summary>
        public float MeasuredReach { get; private set; }

        /// <summary>«Сырое» расстояние камера → правый контроллер (м), без вычитания радиуса.</summary>
        public float MeasuredArmDistance { get; private set; }

        /// <summary>Идёт ли калибровка прямо сейчас.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Профиль, собранный последней калибровкой (или загруженный при старте).</summary>
        public DrumCalibrationProfile CurrentProfile => _profile;

        private DrumCalibrationProfile _profile;
        private Coroutine _routine;
        private bool _uiBound;
        private bool _hasSavedProfile;
        private bool _missingConfigLogged;
        private bool _rightHandSearchDone;

        private void Awake()
        {
            FindReferences();
        }

        private void Start()
        {
            FindReferences();
            SetupUI();
            LoadSavedProfileAndApply();

            if (_autoStartIfNoProfile && !_hasSavedProfile && !IsRunning)
                StartCoroutine(AutoStartRoutine());
        }

        private void OnDestroy()
        {
            if (_startButton != null) _startButton.onClick.RemoveListener(StartCalibration);
            if (_skipButton != null) _skipButton.onClick.RemoveListener(SkipCalibration);
            if (_saveButton != null) _saveButton.onClick.RemoveListener(SaveAndApply);

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        // ------------------------------------------------------------------
        //  Публичный API
        // ------------------------------------------------------------------

        /// <summary>
        /// Запускает двухэтапную калибровку (рост → досягаемость).
        /// </summary>
        public void StartCalibration()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Calibration] Калибровка уже идёт — повторный запуск проигнорирован");
                return;
            }

            FindReferences();

            if (ResolveCameraTransform() == null)
            {
                Debug.LogError("[Calibration] Камера не найдена — калибровка невозможна");
                SetStage(CalibrationStage.Idle, Loc.T(LocKeys.CalibrationFailed));
                OnFinished?.Invoke(false);
                return;
            }

            _profile = null;
            MeasuredHeight = 0f;
            MeasuredReach = 0f;
            MeasuredArmDistance = 0f;
            _rightHandSearchDone = false;

            if (_calibrationCanvas != null)
                _calibrationCanvas.gameObject.SetActive(true);
            if (_saveButton != null)
                _saveButton.gameObject.SetActive(false);
            if (_startButton != null)
                _startButton.gameObject.SetActive(false);

            SetProgress(0f);

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            IsRunning = true;
            _routine = StartCoroutine(CalibrationRoutine());
            Debug.Log("[Calibration] Калибровка запущена");
        }

        /// <summary>
        /// Пропускает калибровку: применяет сохранённый профиль (если есть) либо
        /// оставляет настройки DrumLayoutConfig как есть.
        /// </summary>
        public void SkipCalibration()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            IsRunning = false;
            SetProgress(0f);

            var saved = DrumCalibrationProfile.Load();
            if (saved != null)
            {
                _profile = saved;
                _hasSavedProfile = true;
                MeasuredHeight = saved.PlayerHeight;
                MeasuredReach = Mathf.Clamp(saved.DistanceToDrums, ResolveMinReach(), ResolveMaxReach());

                if (_drumConfig != null)
                    _drumConfig.ApplyProfile(saved);
                else
                    LogMissingConfig();

                TryPositionDrums();
                SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationSkipped));
                Debug.Log($"[Calibration] Пропуск: применён профиль \"{saved.ProfileName}\" (рост {saved.PlayerHeight:F2} м, дистанция {saved.DistanceToDrums:F2} м, {saved.LastCalibrated})");
            }
            else
            {
                TryPositionDrums();
                SetStage(CalibrationStage.Idle, Loc.T(LocKeys.CalibrationSkipped));
                Debug.Log("[Calibration] Пропуск: сохранённого профиля нет, используются настройки DrumLayoutConfig");
            }

            if (_calibrationCanvas != null)
                _calibrationCanvas.gameObject.SetActive(false);

            OnFinished?.Invoke(false);
        }

        /// <summary>
        /// Сохраняет профиль в PlayerPrefs (ключ "ragnaGod.calibration") и закрывает UI.
        /// Если калибровка уже была измерена — повторно применяет значения к сцене.
        /// </summary>
        public void SaveAndApply()
        {
            if (MeasuredHeight > 0f && MeasuredReach > 0f)
                ApplyCalibration();

            if (_profile == null)
            {
                Debug.LogWarning("[Calibration] Профиль не сформирован — сохраняю значения DrumLayoutConfig по умолчанию");
                _profile = _drumConfig != null ? _drumConfig.ToProfile() : DrumCalibrationProfile.CreateDefault();
            }

            if (MeasuredHeight > 0f)
                _profile.PlayerHeight = MeasuredHeight;
            if (MeasuredReach > 0f)
                _profile.DistanceToDrums = MeasuredReach;

            _profile.Save();
            _hasSavedProfile = true;

            Debug.Log($"[Calibration] Профиль сохранён (\"{DrumCalibrationProfile.DefaultPrefsKey}\"): " +
                      $"рост={_profile.PlayerHeight:F2} м, дистанция={_profile.DistanceToDrums:F2} м, " +
                      $"высота={_profile.DrumHeight:F2} м, дата={_profile.LastCalibrated}");

            if (_calibrationCanvas != null)
                _calibrationCanvas.gameObject.SetActive(false);
            if (_startButton != null)
                _startButton.gameObject.SetActive(true);
        }

        /// <summary>
        /// Обновляет DrumLayoutConfig измеренными значениями и переставляет барабаны.
        /// Возвращает true, если значения были применены (хотя бы к конфигу/профилю).
        /// </summary>
        public bool ApplyCalibration()
        {
            if (MeasuredHeight <= 0f || MeasuredReach <= 0f)
            {
                Debug.LogWarning("[Calibration] Нет измеренных значений — применять нечего");
                return false;
            }

            float drumHeight = Mathf.Clamp(MeasuredHeight * 0.55f, 0.5f, 1.3f);
            float distance = Mathf.Clamp(MeasuredReach, ResolveMinReach(), ResolveMaxReach());

            if (_drumConfig != null)
            {
                _drumConfig.DrumHeight = drumHeight;
                _drumConfig.DistanceFromPlayer = distance;

                // Reach moves the drum bank as a unit; the four lanes keep their spacing.
                _drumConfig.ArcRadius = 0f;
            }
            else
            {
                LogMissingConfig();
            }

            _profile = _drumConfig != null ? _drumConfig.ToProfile() : DrumCalibrationProfile.CreateDefault();
            if (_profile == null)
                _profile = DrumCalibrationProfile.CreateDefault();

            _profile.PlayerHeight = MeasuredHeight;
            _profile.DistanceToDrums = distance;
            _profile.DrumHeight = drumHeight;
            if (string.IsNullOrEmpty(_profile.ProfileName))
                _profile.ProfileName = "Player";
            _profile.LastCalibrated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _profile.Sanitize();

            bool positioned = TryPositionDrums();
            if (!positioned)
            {
                // По ТЗ: если DrumManager/DrumPositioner не найдены — просто сохраняем профиль и логируем.
                Debug.LogWarning("[Calibration] Расстановка барабанов не выполнена (нет DrumManager/DrumPositioner) — сохраняю профиль в PlayerPrefs");
                _profile.Save();
                _hasSavedProfile = true;
            }

            Debug.Log($"[Calibration] Применено: высота барабанов={_profile.DrumHeight:F2} м, дистанция={_profile.DistanceToDrums:F2} м, ширина={_profile.TotalWidth:F2} м, дуга={_profile.ArcRadius:F2} м");
            return true;
        }

        // ------------------------------------------------------------------
        //  Корутина калибровки
        // ------------------------------------------------------------------

        private IEnumerator CalibrationRoutine()
        {
            // ---------- Этап 1: рост ----------
            SetStage(CalibrationStage.MeasuringHeight, Loc.T(LocKeys.CalibrationMeasuringHeight));
            SetProgress(0f);

            Transform cameraTransform = ResolveCameraTransform();
            float duration = Mathf.Max(0.5f, _heightMeasureSeconds);
            float elapsed = 0f;
            float headSum = 0f;
            int headSamples = 0;

            while (elapsed < duration)
            {
                if (cameraTransform != null)
                {
                    float headY = cameraTransform.position.y;
                    if (headY > 0.1f)
                    {
                        headSum += headY;
                        headSamples++;
                    }
                }

                elapsed += Time.unscaledDeltaTime;
                SetProgress(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (cameraTransform == null || headSamples == 0)
            {
                Debug.LogError("[Calibration] Не удалось измерить высоту головы (камера/трекинг недоступны)");
                SetStatus(Loc.T(LocKeys.CalibrationFailed));
                SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationFailed));
                Finish(false);
                yield break;
            }

            float headYAverage = headSum / headSamples;
            float playerHeight = headYAverage + 0.12f;

            if (headYAverage < 0.5f)
            {
                Debug.LogError($"[Calibration] Высота головы {headYAverage:F2} м выглядит неверной — калибровка прервана");
                SetStatus(Loc.T(LocKeys.CalibrationFailed));
                SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationFailed));
                Finish(false);
                yield break;
            }

            if (headYAverage < 0.8f)
                Debug.LogWarning($"[Calibration] Низкая высота головы ({headYAverage:F2} м) — проверьте масштаб сцены и трекинг");

            MeasuredHeight = Mathf.Clamp(playerHeight, DrumCalibrationProfile.MinPlayerHeight, DrumCalibrationProfile.MaxPlayerHeight);
            float drumHeight = Mathf.Clamp(MeasuredHeight * 0.55f, 0.5f, 1.3f);

            SetStatus(Loc.T(LocKeys.CalibrationResult,
                MeasuredHeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                drumHeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            SetProgress(1f);
            Debug.Log($"[Calibration] Этап 1: высота глаз {headYAverage:F2} м → рост {MeasuredHeight:F2} м, высота барабанов {drumHeight:F2} м");

            // ---------- Этап 2: досягаемость ----------
            SetStage(CalibrationStage.MeasuringReach, Loc.T(LocKeys.CalibrationMeasuringReach));
            SetProgress(0f);

            // Если триггер ещё зажат после нажатия кнопки «Старт» — сначала ждём отпускания.
            float releaseWait = 0f;
            while (IsRightTriggerHeld() && releaseWait < 2f)
            {
                releaseWait += Time.unscaledDeltaTime;
                yield return null;
            }

            var rightHand = ResolveRightHandDriver();
            if (rightHand != null)
            {
                _rightHandPoseDriver = rightHand;
                if (_rightControllerTransform == null)
                    _rightControllerTransform = rightHand.transform;
            }

            float timeout = Mathf.Max(1f, _reachTimeoutSeconds);
            float waited = 0f;
            bool pressed = false;

            while (waited < timeout)
            {
                if (IsRightTriggerHeld())
                {
                    pressed = true;
                    break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!pressed)
            {
                Debug.LogWarning($"[Calibration] Триггер правого контроллера не нажат за {timeout:F0} с — калибровка не применена");
                SetStatus(Loc.T(LocKeys.CalibrationFailed));
                SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationFailed));
                Finish(false);
                yield break;
            }

            Transform controller = _rightControllerTransform;
            if (controller == null && rightHand != null)
                controller = rightHand.transform;

            if (controller == null)
            {
                Debug.LogError("[Calibration] Правый контроллер не найден — замер дистанции невозможен");
                SetStatus(Loc.T(LocKeys.CalibrationFailed));
                SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationFailed));
                Finish(false);
                yield break;
            }

            cameraTransform = ResolveCameraTransform();
            if (cameraTransform == null)
            {
                Debug.LogError("[Calibration] Камера пропала во время калибровки");
                SetStatus(Loc.T(LocKeys.CalibrationFailed));
                SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationFailed));
                Finish(false);
                yield break;
            }

            if (controller.position.y < 0.2f)
                Debug.LogWarning($"[Calibration] Правый контроллер у пола (y={controller.position.y:F2} м) — возможно, трекинг контроллера не активен");

            MeasuredArmDistance = Vector3.Distance(cameraTransform.position, controller.position);

            float diameter = _drumConfig != null ? _drumConfig.DrumDiameter : 0.35f;
            float minReach = ResolveMinReach();
            float maxReach = ResolveMaxReach();
            float rawReach = MeasuredArmDistance - diameter * 0.5f;
            MeasuredReach = Mathf.Clamp(rawReach, minReach, maxReach);

            if (!Mathf.Approximately(rawReach, MeasuredReach))
                Debug.Log($"[Calibration] Дистанция {rawReach:F2} м ограничена до {MeasuredReach:F2} м (рамки {minReach:F2}..{maxReach:F2})");

            SetStatus(Loc.T(LocKeys.CalibrationResultReach,
                MeasuredHeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                (MeasuredHeight * 0.55f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                MeasuredReach.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
            SetProgress(1f);
            Debug.Log($"[Calibration] Этап 2: камера → контроллер {MeasuredArmDistance:F2} м, " +
                      $"DistanceFromPlayer={MeasuredReach:F2} м (диаметр барабана {diameter:F2} м)");

            SetStage(CalibrationStage.Complete, Loc.T(LocKeys.CalibrationDone));

            bool applied = ApplyCalibration();

            if (_saveButton != null)
                _saveButton.gameObject.SetActive(true);
            if (_startButton != null)
                _startButton.gameObject.SetActive(true);

            Finish(applied);
        }

        private IEnumerator AutoStartRoutine()
        {
            yield return new WaitForSeconds(1f);

            if (!IsRunning && CurrentStage == CalibrationStage.Idle)
                StartCalibration();
        }

        private void Finish(bool applied)
        {
            IsRunning = false;
            _routine = null;

            if (!applied)
                SetProgress(0f);

            OnFinished?.Invoke(applied);
            Debug.Log($"[Calibration] Завершено: applied={applied}, рост={MeasuredHeight:F2} м, дистанция={MeasuredReach:F2} м");
        }

        // ------------------------------------------------------------------
        //  Применение к сцене
        // ------------------------------------------------------------------

        private bool TryPositionDrums()
        {
            if (_drumPositioner == null)
                _drumPositioner = UnityEngine.Object.FindAnyObjectByType<DrumPositioner>();
            if (_drumManager == null)
                _drumManager = UnityEngine.Object.FindAnyObjectByType<DrumManager>();

            if (_drumPositioner == null)
            {
                Debug.LogWarning("[Calibration] DrumPositioner не найден — позиции барабанов не пересчитаны");
                return false;
            }

            if (_drumManager == null)
            {
                Debug.LogWarning("[Calibration] DrumManager не найден — барабаны не расставлены");
                return false;
            }

            if (_drumPositioner.Config == null && _drumConfig != null)
                _drumPositioner.Config = _drumConfig;

            if (!_drumPositioner.IsReady)
            {
                Debug.LogWarning("[Calibration] DrumPositioner не готов (нет DrumLayoutConfig или XR Origin) — расстановка пропущена");
                return false;
            }

            if (!HasDrums(_drumManager))
            {
                // Обычно барабаны создаёт DrumSceneBootstrap; здесь — страховка на случай другого порядка Start().
                _drumManager.CreateDrums();

                if (!HasDrums(_drumManager))
                {
                    Debug.LogWarning("[Calibration] Барабаны не созданы (проверьте Drum Prefab) — расстановка пропущена");
                    return false;
                }
            }

            _drumPositioner.Calculate();
            _drumManager.PositionDrums(_drumPositioner.DrumPositions, _drumPositioner.DrumRotation);

            float height = _drumConfig != null ? _drumConfig.DrumHeight : 0f;
            float distance = _drumConfig != null ? _drumConfig.DistanceFromPlayer : 0f;
            Debug.Log($"[Calibration] Барабаны расставлены: высота {height:F2} м, дистанция {distance:F2} м");
            return true;
        }

        private static bool HasDrums(DrumManager manager)
        {
            if (manager == null)
                return false;

            var drums = manager.Drums;
            return drums != null && drums.Length > 0 && drums[0] != null;
        }

        private void LoadSavedProfileAndApply()
        {
            var saved = DrumCalibrationProfile.Load();
            if (saved == null)
            {
                _hasSavedProfile = false;

                if (_calibrationCanvas == null && _startButton == null && !_autoStartIfNoProfile)
                {
                    Debug.Log("[Calibration] Сохранённого профиля нет, UI калибровки не назначен. " +
                              "Вызовите StartCalibration() из кнопки/скрипта или включите _autoStartIfNoProfile");
                }
                else
                {
                    Debug.Log("[Calibration] Сохранённый профиль не найден — используются настройки DrumLayoutConfig");
                }

                return;
            }

            _hasSavedProfile = true;
            _profile = saved;
            MeasuredHeight = saved.PlayerHeight;
            MeasuredReach = Mathf.Clamp(saved.DistanceToDrums, ResolveMinReach(), ResolveMaxReach());

            if (_drumConfig != null)
                _drumConfig.ApplyProfile(saved);
            else
                LogMissingConfig();

            TryPositionDrums();

            CurrentStage = CalibrationStage.Complete;
            Debug.Log($"[Calibration] Применён сохранённый профиль \"{saved.ProfileName}\": рост {saved.PlayerHeight:F2} м, " +
                      $"дистанция {saved.DistanceToDrums:F2} м, высота {saved.DrumHeight:F2} м, дата {saved.LastCalibrated}");
        }

        // ------------------------------------------------------------------
        //  Ссылки и устройства
        // ------------------------------------------------------------------

        private void FindReferences()
        {
            if (_xrOrigin == null)
            {
                var originGo = GameObject.Find("XR Origin");
                if (originGo != null)
                    _xrOrigin = originGo.transform;
            }

            if (_cameraTransform == null && _xrOrigin != null)
            {
                var originCamera = _xrOrigin.GetComponentInChildren<Camera>();
                if (originCamera != null)
                    _cameraTransform = originCamera.transform;
            }

            if (_cameraTransform == null)
            {
                var mainCamera = Camera.main;
                if (mainCamera != null)
                    _cameraTransform = mainCamera.transform;
            }

            if (_drumPositioner == null)
                _drumPositioner = GetComponent<DrumPositioner>();
            if (_drumPositioner == null)
                _drumPositioner = UnityEngine.Object.FindAnyObjectByType<DrumPositioner>();

            if (_drumManager == null)
                _drumManager = GetComponent<DrumManager>();
            if (_drumManager == null)
                _drumManager = UnityEngine.Object.FindAnyObjectByType<DrumManager>();

            // Конфиг: сериализованное поле → конфиг позиционера → Resources.
            if (_drumConfig == null && _drumPositioner != null)
                _drumConfig = _drumPositioner.Config;

            if (_drumConfig == null)
                _drumConfig = Resources.Load<DrumLayoutConfig>("DrumLayoutConfig");

            if (_drumConfig != null && _drumPositioner != null && _drumPositioner.Config == null)
                _drumPositioner.Config = _drumConfig;

            if (_rightHandPoseDriver == null)
                _rightHandPoseDriver = ResolveRightHandDriver();

            if (_rightControllerTransform == null && _rightHandPoseDriver != null)
                _rightControllerTransform = _rightHandPoseDriver.transform;
        }

        private Transform ResolveCameraTransform()
        {
            if (_cameraTransform != null)
                return _cameraTransform;

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _cameraTransform = mainCamera.transform;
                return _cameraTransform;
            }

            if (_xrOrigin != null)
            {
                var originCamera = _xrOrigin.GetComponentInChildren<Camera>();
                if (originCamera != null)
                    _cameraTransform = originCamera.transform;
            }

            return _cameraTransform;
        }

        private XRHandPoseDriver ResolveRightHandDriver()
        {
            if (_rightHandPoseDriver != null && _rightHandPoseDriver.Side == XRHandPoseDriver.Hand.Right)
                return _rightHandPoseDriver;

            var drivers = UnityEngine.Object.FindObjectsByType<XRHandPoseDriver>();
            foreach (var driver in drivers)
            {
                if (driver != null && driver.Side == XRHandPoseDriver.Hand.Right)
                    return driver;
            }

            return null;
        }

        private bool IsRightTriggerHeld()
        {
            if (_rightHandPoseDriver == null && !_rightHandSearchDone)
            {
                _rightHandSearchDone = true;
                _rightHandPoseDriver = ResolveRightHandDriver();
            }

            if (_rightHandPoseDriver != null)
            {
                if (_rightControllerTransform == null)
                    _rightControllerTransform = _rightHandPoseDriver.transform;

                return _rightHandPoseDriver.IsTriggerPressed;
            }

            return IsRightTriggerPressedViaXR();
        }

        /// <summary>
        /// Резервный путь, если XRHandPoseDriver в сцене не найден:
        /// читаем triggerButton напрямую из UnityEngine.XR (без UnityEngine.Input).
        /// </summary>
        private static bool IsRightTriggerPressedViaXR()
        {
            var devices = new List<InputDevice>();
            InputDevices.GetDevicesAtXRNode(XRNode.RightHand, devices);

            foreach (var device in devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
                    return true;
            }

            return false;
        }

        private float ResolveMinReach()
        {
            return Mathf.Min(_minReach, _maxReach);
        }

        private float ResolveMaxReach()
        {
            return Mathf.Max(_minReach, _maxReach);
        }

        // ------------------------------------------------------------------
        //  UI (все поля необязательны)
        // ------------------------------------------------------------------

        private void SetupUI()
        {
            if (!_uiBound)
            {
                _uiBound = true;

                if (_startButton != null)
                    _startButton.onClick.AddListener(StartCalibration);
                if (_skipButton != null)
                    _skipButton.onClick.AddListener(SkipCalibration);
                if (_saveButton != null)
                    _saveButton.onClick.AddListener(SaveAndApply);
            }

            if (_saveButton != null)
                _saveButton.gameObject.SetActive(false);

            if (_calibrationCanvas != null)
                _calibrationCanvas.gameObject.SetActive(false);

            SetProgress(0f);
        }

        private void SetStage(CalibrationStage stage, string instruction)
        {
            CurrentStage = stage;

            if (_instructionText != null && !string.IsNullOrEmpty(instruction))
                _instructionText.text = instruction;

            OnStageChanged?.Invoke(stage.ToString());

            string flatInstruction = string.IsNullOrEmpty(instruction) ? string.Empty : instruction.Replace("\n", " ");
            Debug.Log($"[Calibration] Этап: {stage}" + (string.IsNullOrEmpty(flatInstruction) ? string.Empty : $" — {flatInstruction}"));
        }

        private void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);

            if (_progressFill != null)
                _progressFill.fillAmount = value;
            if (_progressText != null)
                _progressText.text = $"{value * 100f:F0}%";

            OnProgressChanged?.Invoke(value);
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;

            if (!string.IsNullOrEmpty(text))
                Debug.Log($"[Calibration] {text.Replace("\n", " ")}");
        }

        private void LogMissingConfig()
        {
            if (_missingConfigLogged)
                return;

            _missingConfigLogged = true;
            Debug.LogWarning("[Calibration] DrumLayoutConfig не найден — калибровка обновит только профиль и не сможет расставить барабаны");
        }
    }
}
