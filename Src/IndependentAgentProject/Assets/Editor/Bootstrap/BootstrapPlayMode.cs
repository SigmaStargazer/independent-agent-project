using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class BootstrapPlayMode
{
    private const string LastSceneKey = "LastOpenedScene";

    static BootstrapPlayMode()
    {
        // 按下play 键时调用
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(
        PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            // 当前真正打开的Scene
            string currentScene = EditorSceneManager.GetActiveScene().path;
            // 记录下来
            EditorPrefs.SetString(LastSceneKey, currentScene);
            // 强制PlayMode从Bootstrap启动
            var bootstrap =AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Bootstrap.unity");
            EditorSceneManager.playModeStartScene =bootstrap;
        }
    }
}
