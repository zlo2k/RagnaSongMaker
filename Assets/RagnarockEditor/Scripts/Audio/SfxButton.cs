using UnityEngine;

namespace RagnarockEditor.Audio
{
    /// <summary>
    /// Какой UI-звук играть при нажатии на эту uGUI-кнопку.
    /// Фиксер вешает компонент на все кнопки (по умолчанию Select),
    /// а для диалогов/экспорта ставит Confirm и Back.
    /// </summary>
    public class SfxButton : MonoBehaviour
    {
        [SerializeField] private SfxPlayer.UiSound _sound = SfxPlayer.UiSound.Select;

        public SfxPlayer.UiSound Sound => _sound;

        public void SetSound(SfxPlayer.UiSound sound) => _sound = sound;
    }
}
