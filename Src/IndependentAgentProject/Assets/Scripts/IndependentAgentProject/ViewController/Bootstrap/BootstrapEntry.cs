using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class BootstrapEntry : MonoBehaviour
{
    private async void Start()
    {
        await Initialize();

#if UNITY_EDITOR

        // 编辑器模式：
        // 返回原本测试Scene
        string lastScene = EditorPrefs.GetString("LastOpenedScene","");

        // 防止死循环
        if (!string.IsNullOrEmpty(lastScene) && !lastScene.Contains("Bootstrap"))
        {
            SceneManager.LoadScene(lastScene);
            return;
        }

#endif

        // 正式游戏：
        // 进入标题
        SceneManager.LoadScene("Title");
    }

    private async UniTask Initialize()
    {
        // 初始化全局系统
        await UniTask.Yield();
    }
}