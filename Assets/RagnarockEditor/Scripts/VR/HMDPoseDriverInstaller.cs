using UnityEngine;
using UnityEngine.SceneManagement;

namespace RagnarockEditor.VR
{
    /// <summary>
    /// Страховка на рантайме: в каждой загруженной сцене у главной камеры должен быть
    /// HMDPoseDriver. Работает даже если сцены не правились в редакторе.
    /// </summary>
    public static class HMDPoseDriverInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            EnsureDriver();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureDriver();
        }

        private static void EnsureDriver()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            if (camera.GetComponent<HMDPoseDriver>() == null)
                camera.gameObject.AddComponent<HMDPoseDriver>();
        }
    }
}
