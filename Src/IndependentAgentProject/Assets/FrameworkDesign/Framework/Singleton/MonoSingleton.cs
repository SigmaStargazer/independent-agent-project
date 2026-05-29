using UnityEngine;

namespace FrameworkDesign
{
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<T>();
                    if (instance == null)
                    {
                        Debug.LogError($"[MonoSingleton] {typeof(T).Name} not found.");
                    }
                }
                return instance;
            }
        }

        [SerializeField]
        private bool dontDestroyOnLoad = false;

        protected virtual void Awake()
        {
            // 已存在实例
            if (instance != null && instance != this)
            {
                Debug.LogWarning(
                    $"Duplicate singleton: {typeof(T).Name} on {gameObject.name}"
                );

                Destroy(gameObject);
                return;
            }

            instance = this as T;

            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
    }
}