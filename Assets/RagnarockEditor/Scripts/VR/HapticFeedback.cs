using UnityEngine;
using UnityEngine.XR;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Управляет тактильной отдачей на контроллерах.
    /// Интенсивность зависит от силы удара.
    /// </summary>
    public static class HapticFeedback
    {
        /// <summary>
        /// Запускает тактильный импульс на указанном контроллере.
        /// </summary>
        /// <param name="isRightHand">true = правый контроллер, false = левый</param>
        /// <param name="hitVelocity">Скорость удара (м/с) для расчёта интенсивности</param>
        /// <param name="maxVelocity">Скорость, соответствующая максимальной интенсивности</param>
        public static void TriggerHaptic(bool isRightHand, float hitVelocity, float maxVelocity = 10f)
        {
            // Нормализуем скорость удара в амплитуду (0-1)
            float amplitude = Mathf.Clamp01(hitVelocity / maxVelocity);
            
            // Минимальная амплитуда для ощутимого haptic
            if (amplitude < 0.1f) return;

            // Длительность импульса (50-150 мс в зависимости от силы)
            float duration = Mathf.Lerp(0.05f, 0.15f, amplitude);

            // Отправляем haptic на нужное устройство
            InputDevice device = GetControllerDevice(isRightHand);
            if (device.isValid)
            {
                device.SendHapticImpulse(0u, amplitude, duration);
            }
        }

        /// <summary>
        /// Мягкий тактильный импульс (для UI или лёгкого касания).
        /// </summary>
        public static void LightHaptic(bool isRightHand)
        {
            InputDevice device = GetControllerDevice(isRightHand);
            if (device.isValid)
            {
                device.SendHapticImpulse(0u, 0.2f, 0.03f);
            }
        }

        /// <summary>
        /// Останавливает всю тактильную отдачу.
        /// </summary>
        public static void StopHaptics()
        {
            InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            if (leftDevice.isValid) leftDevice.StopHaptics();
            if (rightDevice.isValid) rightDevice.StopHaptics();
        }

        /// <summary>
        /// Возвращает InputDevice для указанной руки.
        /// </summary>
        private static InputDevice GetControllerDevice(bool isRightHand)
        {
            XRNode node = isRightHand ? XRNode.RightHand : XRNode.LeftHand;
            return InputDevices.GetDeviceAtXRNode(node);
        }

        /// <summary>
        /// Проверяет, поддерживает ли устройство haptic.
        /// </summary>
        public static bool IsHapticSupported(bool isRightHand)
        {
            InputDevice device = GetControllerDevice(isRightHand);
            if (!device.isValid) return false;

            return device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse;
        }
    }
}
