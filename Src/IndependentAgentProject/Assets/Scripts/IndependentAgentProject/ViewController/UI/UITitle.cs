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
        [Header("配置子面板")]
        [SerializeField]
        private GameObject mLLMAgentPanel;
        [SerializeField]
        private GameObject mLLMMemoryPanel;
        [SerializeField]
        private GameObject mEmbeddingPanel;
        [SerializeField]
        private GameObject mRerankerPanel;
        [Header("新游戏弹窗")]
        [SerializeField]
        private GameObject mNewGameWarmingMsgbox;
        [Header("保存配置确认弹窗")]
        [SerializeField]
        private GameObject mSaveConfigMsgBox;
        [Header("无API Key弹窗")]
        [SerializeField]
        private GameObject mNoApiKeyMsgbox;
        [Header("退出游戏弹窗")]
        [SerializeField]
        private GameObject mQuitMsgbox;


        [Header("输入消抖窗口（秒），防止 ESC 返回时被 anyKeyDown 立刻切回")]
        [SerializeField]
        private float mInputLockTime = 0.25f;
        private float mInputLockUntil;
        private bool InLockWindow => Time.time < mInputLockUntil;

        void Awake()
        {
            if (mNewGameWarmingMsgbox != null)
                mNewGameWarmingMsgbox.SetActive(false);
            if (mNoApiKeyMsgbox != null)
                mNoApiKeyMsgbox.SetActive(false);
            if (mQuitMsgbox != null)
                mQuitMsgbox.SetActive(false);
            if (mSaveConfigMsgBox != null)
                mSaveConfigMsgBox.SetActive(false);
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

            // 有 MsgBox 弹窗打开时，ESC 由 UIMsgBox 接管（触发 Btn1），UITitle 不处理
            if (UIMsgBox.AnyActive)
            {
                return;
            }

            // 统一 ESC / 任意键 分发
            if (mPressAnyButtonPanel != null && mPressAnyButtonPanel.activeSelf)
            {
                // 按任意按钮：任意键/鼠标/手柄 进入主菜单（ESC 亦作为普通按键）
                if (Input.anyKeyDown)
                {
                    ShowMainMenu();
                }
            }
            else if (IsSubPanelActive())
            {
                // 配置子面板：ESC 弹出保存确认
                if (Input.GetButtonDown("Menu"))
                {
                    ShowSaveConfigMsgBox();
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
            SetPanelActive(mLLMAgentPanel, false);
            SetPanelActive(mLLMMemoryPanel, false);
            SetPanelActive(mEmbeddingPanel, false);
            SetPanelActive(mRerankerPanel, false);
            LockInput();
        }

        /// <summary>切换到主菜单面板。</summary>
        public void ShowMainMenu()
        {
            SetPanelActive(mPressAnyButtonPanel, false);
            SetPanelActive(mMainMenuPanel, true);
            SetPanelActive(mConfigPanel, false);
            SetPanelActive(mLLMAgentPanel, false);
            SetPanelActive(mLLMMemoryPanel, false);
            SetPanelActive(mEmbeddingPanel, false);
            SetPanelActive(mRerankerPanel, false);
            LockInput();
        }

        /// <summary>切换到设置面板（关闭 4 个配置子面板，从任一子面板返回设置总览）。</summary>
        public void ShowConfig()
        {
            SetPanelActive(mPressAnyButtonPanel, false);
            SetPanelActive(mMainMenuPanel, false);
            SetPanelActive(mLLMAgentPanel, false);
            SetPanelActive(mLLMMemoryPanel, false);
            SetPanelActive(mEmbeddingPanel, false);
            SetPanelActive(mRerankerPanel, false);
            SetPanelActive(mConfigPanel, true);
            LockInput();
        }

        /// <summary>打开「LLM Agent」配置子面板。</summary>
        public void OnClickLLMAgentConfig()
        {
            SetSubPanelActive(mLLMAgentPanel);
        }

        /// <summary>打开「LLM Memory」配置子面板。</summary>
        public void OnClickLLMMemoryConfig()
        {
            SetSubPanelActive(mLLMMemoryPanel);
        }

        /// <summary>打开「Embedding」配置子面板。</summary>
        public void OnClickEmbeddingConfig()
        {
            SetSubPanelActive(mEmbeddingPanel);
        }

        /// <summary>打开「Reranker」配置子面板。</summary>
        public void OnClickRerankerConfig()
        {
            SetSubPanelActive(mRerankerPanel);
        }

        /// <summary>确认保存：关闭保存确认弹窗并返回设置总览（实际保存逻辑后续版本实现）。</summary>
        public void OnConfirmSaveConfig()
        {
            if (mSaveConfigMsgBox != null)
                mSaveConfigMsgBox.SetActive(false);
            ShowConfig();
        }

        /// <summary>取消保存：仅关闭保存确认弹窗，停留当前配置子面板。</summary>
        public void OnCancelSaveConfig()
        {
            if (mSaveConfigMsgBox != null)
                mSaveConfigMsgBox.SetActive(false);
        }

        private void SetSubPanelActive(GameObject subPanel)
        {
            SetPanelActive(mLLMAgentPanel, subPanel == mLLMAgentPanel);
            SetPanelActive(mLLMMemoryPanel, subPanel == mLLMMemoryPanel);
            SetPanelActive(mEmbeddingPanel, subPanel == mEmbeddingPanel);
            SetPanelActive(mRerankerPanel, subPanel == mRerankerPanel);
            SetPanelActive(mConfigPanel, false);
            LockInput();
        }

        private bool IsSubPanelActive()
        {
            return (mLLMAgentPanel != null && mLLMAgentPanel.activeSelf)
                || (mLLMMemoryPanel != null && mLLMMemoryPanel.activeSelf)
                || (mEmbeddingPanel != null && mEmbeddingPanel.activeSelf)
                || (mRerankerPanel != null && mRerankerPanel.activeSelf);
        }

        private void ShowSaveConfigMsgBox()
        {
            if (mSaveConfigMsgBox != null)
            {
                mSaveConfigMsgBox.SetActive(true);
                LockInput();
            }
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
