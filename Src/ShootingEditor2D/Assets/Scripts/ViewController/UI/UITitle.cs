using Cysharp.Threading.Tasks;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootingEditor2D
{
    public class UITitle : MonoBehaviour
    {
        public string firstLevelName = "Level1";
        public string agentName = "小明";
        public string agentDesc = "是一个帮助机器人";

        public GameObject warningPanel;

        void Awake()
        {
            warningPanel.SetActive(false);
        }
        void Start()
        {
            //AgentService.Instance.OnLoadAgent = this.OnLoadAgent;
            //AgentService.Instance.OnStartScene = this.OnStartScene;
        }

        void Update()
        {

        }

        public void OnClickNewGame()
        {
            GameFlowManager.Instance.StartNewGame(firstLevelName, agentName, agentDesc).Forget(Debug.LogException);
        }

        public void OnClickContinueGame()
        {
            GameFlowManager.Instance.ContinueGame().Forget(Debug.LogException);
        }
    }
}
