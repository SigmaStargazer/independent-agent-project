using Cysharp.Threading.Tasks;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

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
        private string mAgentDesc = "小明是一个帮助机器人";
        [Header("按任意键面板")]
        [SerializeField]
        private GameObject mPressAnyButtonPanel;
        [Header("主菜单面板")]
        [SerializeField]
        private GameObject mMainMenuPanel;
        [Header("设置面板")]
        [SerializeField]
        private GameObject mConfigPanel;
        [Header("新游戏弹窗")]
        [SerializeField]
        private GameObject mNewGameWarmingPanel;
        [Header("无API Key弹窗")]
        [SerializeField]
        private GameObject mNoApiKeyPanel;
        [Header("退出游戏弹窗")]
        [SerializeField]
        private GameObject mQuitPanel;

        [Header("输入消抖窗口（秒），防止 ESC 返回时被 anyKeyDown 立刻切回")]
        [SerializeField]
        private float mInputLockTime = 0.25f;
        private float mInputLockUntil;
        private bool InLockWindow => Time.time < mInputLockUntil;

        void Awake()
        {
            if (mNewGameWarmingPanel != null)
                mNewGameWarmingPanel.SetActive(false);
            if (mNoApiKeyPanel != null)
                mNoApiKeyPanel.SetActive(false);
            if (mQuitPanel != null)
                mQuitPanel.SetActive(false);
        }
        void Start()
        {
            // 初始状态：显示「按任意按钮」，隐藏其余面板
            ShowPressAnyButton();
        }

        void Update()
        {
            if (InLockWindow)
            {
                return;
            }

            // 统一 ESC / 任意键 分发（方案 A：UITitle 总控）
            if (mPressAnyButtonPanel != null && mPressAnyButtonPanel.activeSelf)
            {
                // 按任意按钮：任意键/鼠标/手柄 进入主菜单（ESC 亦作为普通按键）
                if (Input.anyKeyDown)
                {
                    ShowMainMenu();
                }
            }
            else if (mConfigPanel != null && mConfigPanel.activeSelf)
            {
                // 设置：ESC 返回主菜单
                if (Input.GetButtonDown("Menu"))
                {
                    ShowMainMenu();
                }
            }
            else if (mMainMenuPanel != null && mMainMenuPanel.activeSelf)
            {
                // 主菜单：ESC 返回「按任意按钮」
                if (Input.GetButtonDown("Menu"))
                {
                    ShowPressAnyButton();
                }
            }
        }

        /// <summary>切换到「按任意按钮」面板。</summary>
        public void ShowPressAnyButton()
        {
            SetPanelActive(mPressAnyButtonPanel, true);
            SetPanelActive(mMainMenuPanel, false);
            SetPanelActive(mConfigPanel, false);
            LockInput();
        }

        /// <summary>切换到主菜单面板。</summary>
        public void ShowMainMenu()
        {
            SetPanelActive(mPressAnyButtonPanel, false);
            SetPanelActive(mMainMenuPanel, true);
            SetPanelActive(mConfigPanel, false);
            LockInput();
        }

        /// <summary>切换到设置面板。</summary>
        public void ShowConfig()
        {
            SetPanelActive(mPressAnyButtonPanel, false);
            SetPanelActive(mMainMenuPanel, false);
            SetPanelActive(mConfigPanel, true);
            LockInput();
        }

        private void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null)
                panel.SetActive(active);
        }

        private void LockInput()
        {
            mInputLockUntil = Time.time + mInputLockTime;
        }

        public void OnClickNewGame()
        {
            GameFlowManager.Instance.StartNewGame(mFirstLevelName, mAgentName, mAgentDesc).Forget(Debug.LogException);
        }

        public void OnClickContinueGame()
        {
            GameFlowManager.Instance.ContinueGame().Forget(Debug.LogException);
        }

        public void OnClickConfig()
        {
            ShowConfig();
        }

        /// <summary>退出游戏。构建版结束进程；编辑器下退出 Play 模式。</summary>
        public void OnClickQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
