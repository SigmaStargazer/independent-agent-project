using Cysharp.Threading.Tasks;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndependentAgentProject
{
    public class UITitle : MonoBehaviour
    {
        [Header("第一关名称")]
        [SerializeField]
        private string mFirstLevelName = "Level1";
        [Header("创建Agent配置")]
        [SerializeField]
        private string mAgentName = "小明";
        [SerializeField]
        private string mAgentDesc = "是一个帮助机器人";
        [Header("新游戏弹窗")]
        [SerializeField]
        private GameObject mNewGameWarmimhPanel;

        void Awake()
        {
            mNewGameWarmimhPanel.SetActive(false);
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
            GameFlowManager.Instance.StartNewGame(mFirstLevelName, mAgentName, mAgentDesc).Forget(Debug.LogException);
        }

        public void OnClickContinueGame()
        {
            GameFlowManager.Instance.ContinueGame().Forget(Debug.LogException);
        }
    }
}
