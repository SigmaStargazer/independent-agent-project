using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Services;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class BootstrapEntry : MonoBehaviour
{
    private async void Start()
    {
        await Initialize();

        // v0.23.3b：打包版启动即拉起 Python exe（无窗口）。编辑器下不拉起（由开发者手动起 Python）。
        PythonProcessLauncher.Launch();

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

    /// <summary>
    /// v0.23.3b：Unity 进程退出（正常/异常/强制）时清理 Python 子进程。
    /// 返回 Title 场景不在此清理（仅 SceneStop，Python 进程保持存活）。
    /// </summary>
    private void OnApplicationQuit()
    {
        PythonProcessLauncher.Shutdown();
    }
}