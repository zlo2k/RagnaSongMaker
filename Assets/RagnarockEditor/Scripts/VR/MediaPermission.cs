using UnityEngine;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Разрешение на чтение музыки: READ_MEDIA_AUDIO на Android 13+,
    /// READ_EXTERNAL_STORAGE на более старых. В редакторе считается выданным.
    /// </summary>
    public static class MediaPermission
    {
        public static string PermissionName
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    var sdk = version.GetStatic<int>("SDK_INT");
                    return sdk >= 33
                        ? "android.permission.READ_MEDIA_AUDIO"
                        : "android.permission.READ_EXTERNAL_STORAGE";
                }
#else
                return string.Empty;
#endif
            }
        }

        public static bool IsGranted
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(PermissionName);
#else
                return true;
#endif
            }
        }

        public static void Request()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log("[Songs] запрашиваю " + PermissionName);
            UnityEngine.Android.Permission.RequestUserPermission(PermissionName);
#endif
        }
    }
}
