using UnityEngine;

namespace RagnarockEditor.Core
{
    /// <summary>
    /// Маркер непрозрачного для луча объекта. Если луч руки попадает в такой коллайдер,
    /// он останавливается и не нажимает кнопки, лежащие за ним (модальный диалог,
    /// фон панели). Ставится на Image фона вместе с BoxCollider.
    /// </summary>
    public class UIRaycastBlocker : MonoBehaviour
    {
    }
}
