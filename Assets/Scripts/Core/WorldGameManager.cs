using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SG
{
    public class WorldGameManager : MonoBehaviour
    {
        public static WorldGameManager instance;

        [Header("Scene Settings")]
        [SerializeField] private int worldSceneIndex = 1;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public IEnumerator LoadNewGame()
        {
            // 1. Wait until the host is active and the NetworkManager is listening
            yield return new WaitUntil(() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);

            // 2. Convert the build index to the scene name/path for Netcode's SceneManager
            string scenePath = SceneUtility.GetScenePathByBuildIndex(worldSceneIndex);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

            // 3. Load the scene via Netcode's NetworkSceneManager
            SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(
                sceneName,
                LoadSceneMode.Single
            );

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[WorldGameManager] Failed to load network scene '{sceneName}'. Status: {status}");
            }
        }
    }
}