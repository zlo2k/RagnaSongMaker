using RagnarockEditor.Audio;
using UnityEngine;
using RagnarockEditor.Core;
using RagnarockEditor.Playback;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// 3D-кнопка из набора Stone UI: объёмная плита, BoxCollider на корне,
    /// подсветка при наведении лучом и действие по нажатию триггера.
    ///
    /// Кнопка не решает сама, можно ли уходить из сцены: Back/Quit/JumpScene идут
    /// через ScreenRouter и AppFlow, поэтому предупреждение о несохранённых данных
    /// одинаково работает на всех экранах.
    /// </summary>
    public class StoneButton : MonoBehaviour
    {
        public enum Action
        {
            JumpScene = 0,
            Quit = 1,
            Play = 2,
            Pause = 3,
            Stop = 4,
            Restart = 5,
            Export = 6,
            Calibrate = 7,
            Back = 8,
            Help = 9,
            NewLevel = 10,
        }

        [SerializeField] private Action _action = Action.JumpScene;
        [SerializeField] private string _sceneName = "MainMenuScene";
        [SerializeField] private Color _highlight = new Color(1f, 0.86f, 0.62f, 1f);

        private Renderer[] _renderers;
        private Color[] _baseColors;
        private MaterialPropertyBlock _block;
        private bool _highlighted;

        /// <summary>Действие кнопки (для отладки и роутера).</summary>
        public Action ButtonAction => _action;

        /// <summary>Имя сцены для JumpScene.</summary>
        public string TargetScene => _sceneName;

        /// <summary>Кнопка видна игроку.</summary>
        public bool IsVisible => gameObject.activeSelf;

        public void Configure(Action action, string sceneName)
        {
            _action = action;
            _sceneName = sceneName ?? string.Empty;
        }

        /// <summary>Показывает/прячет плиту. Спрятанную кнопку луч не видит — коллайдер выключен вместе с объектом.</summary>
        public void SetVisible(bool value)
        {
            if (gameObject.activeSelf == value)
                return;

            if (!value && _highlighted)
                SetHighlighted(false);

            gameObject.SetActive(value);
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++)
            {
                var shared = _renderers[i].sharedMaterial;
                _baseColors[i] = shared != null && shared.HasProperty("_BaseColor")
                    ? shared.GetColor("_BaseColor")
                    : Color.white;
            }
        }

        public void SetHighlighted(bool value)
        {
            if (_highlighted == value || _renderers == null)
                return;

            _highlighted = value;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;

                _renderers[i].GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", value ? _highlight : _baseColors[i]);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        /// <summary>Звук нажатия, подобранный под действие плиты.</summary>
        private void PlayPressSound()
        {
            SfxPlayer.UiSound sound;

            switch (_action)
            {
                case Action.Pause:
                    sound = SfxPlayer.UiSound.Pause;
                    break;

                case Action.Stop:
                case Action.Back:
                case Action.Quit:
                    sound = SfxPlayer.UiSound.Back;
                    break;

                case Action.Restart:
                    sound = SfxPlayer.UiSound.SeekTick;
                    break;

                case Action.Export:
                    sound = SfxPlayer.UiSound.Confirm;
                    break;

                default:
                    sound = SfxPlayer.UiSound.Select;
                    break;
            }

            SfxPlayer.Instance.PlayUi(sound);
        }

        public void Press()
        {
            if (!gameObject.activeInHierarchy)
                return;

            Debug.Log("[StoneUI] " + name + " -> " + _action);

            PlayPressSound();

            switch (_action)
            {
                case Action.JumpScene:
                    if (!string.IsNullOrEmpty(_sceneName))
                        AppFlow.Instance.TryLeaveTo(_sceneName);
                    break;

                case Action.Quit:
                    AppFlow.Instance.TryQuit();
                    break;

                case Action.Back:
                    ScreenRouter.Back();
                    break;

                case Action.Help:
                    ScreenRouter.Help();
                    break;

                case Action.NewLevel:
                    AppFlow.Instance.TryStartNewLevel();
                    break;

                case Action.Export:
                    ScreenRouter.Export();
                    break;

                case Action.Play:
                    ScreenRouter.Play();
                    break;

                case Action.Pause:
                    ScreenRouter.Pause();
                    break;

                case Action.Stop:
                    ScreenRouter.Stop();
                    break;

                case Action.Restart:
                    ScreenRouter.Restart();
                    break;

                case Action.Calibrate:
                    var calibration = Object.FindFirstObjectByType<DrumCalibrationManager>();
                    if (calibration != null)
                        calibration.StartCalibration();
                    else
                        Debug.LogWarning("[StoneUI] DrumCalibrationManager не найден");
                    break;
            }
        }
    }

    /// <summary>
    /// Единая точка маршрутизации действий интерфейса. Stone-кнопки и uGUI-кнопки
    /// зовут ScreenRouter, а он решает, какой экран сейчас активен.
    /// </summary>
    public static class ScreenRouter
    {
        /// <summary>Кнопка «Назад»: закрыть диалог → выйти из BPM → выйти из сцены.</summary>
        public static void Back()
        {
            if (ConfirmDialog.Instance != null && ConfirmDialog.Instance.IsOpen)
            {
                ConfirmDialog.Instance.Hide();
                return;
            }

            var record = Object.FindFirstObjectByType<RecordSessionController>();
            if (record != null && record.HandleBack())
                return;

            var songs = Object.FindFirstObjectByType<SongSelectionScreen>();
            if (songs != null && songs.HandleBack())
                return;

            var maps = Object.FindFirstObjectByType<MyMapsScreen>();
            if (maps != null && maps.Close()) return;

            var intro = Object.FindFirstObjectByType<IntroScreen>();
            if (intro != null && intro.HandleBack())
                return;

            AppFlow.Instance.TryLeaveTo(AppFlow.MainMenuScene);
        }

        /// <summary>«Как пользоваться» — снова показать вводный экран.</summary>
        public static void Help()
        {
            var intro = Object.FindFirstObjectByType<IntroScreen>(FindObjectsInactive.Include);
            if (intro != null)
                intro.ShowIntro();
            else
                Debug.LogWarning("[Router] IntroScreen не найден в сцене");
        }

        public static void Play()
        {
            var record = Object.FindFirstObjectByType<RecordSessionController>();
            if (record != null)
            {
                record.StartSession();
                return;
            }

            var playback = Object.FindFirstObjectByType<PlaybackController>();
            playback?.Play();
        }

        public static void Pause()
        {
            var record = Object.FindFirstObjectByType<RecordSessionController>();
            if (record != null)
            {
                record.TogglePause();
                return;
            }

            var playback = Object.FindFirstObjectByType<PlaybackController>();
            playback?.TogglePause();
        }

        public static void Stop()
        {
            var record = Object.FindFirstObjectByType<RecordSessionController>();
            if (record != null)
            {
                record.StopSession();
                return;
            }

            var playback = Object.FindFirstObjectByType<PlaybackController>();
            playback?.Stop();
        }

        public static void Restart()
        {
            var record = Object.FindFirstObjectByType<RecordSessionController>();
            if (record != null)
            {
                record.RestartSession();
                return;
            }

            var playback = Object.FindFirstObjectByType<PlaybackController>();
            if (playback != null)
            {
                playback.Stop();
                playback.Play();
            }
        }

        public static void Export()
        {
            AppFlow.Instance.SaveThen(null);
        }
    }
}
