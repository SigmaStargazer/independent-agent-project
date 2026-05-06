using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootingEditor2D
{
    public class UITitle : MonoBehaviour
    {
        public string firstLevelName = "Level1";
        void Start()
        {
            //AgentService.Instance.OnLoadAgent = this.OnLoadAgent;
            //AgentService.Instance.OnStartScene = this.OnStartScene;
        }

        void Update()
        {

        }

        #region NewGame
        public void OnClickNewGame()
        {
            // 1. 初始化存档
            SaveManager.Instance.Init(this.firstLevelName);
            // 2. 加载初始场景
            SceneManager.LoadScene(this.firstLevelName);
        }

        public void OnNewGame()
        {

        }
        #endregion NewGame

        public void OnClickContinueGame()
        {
            // 1. 加载存档
            SaveData saveData = SaveManager.Instance.Load();
            // 2. 加载存档的场景名
            var levelName = saveData.LevelName;
            SceneManager.LoadScene(levelName);

        }
    }
}
