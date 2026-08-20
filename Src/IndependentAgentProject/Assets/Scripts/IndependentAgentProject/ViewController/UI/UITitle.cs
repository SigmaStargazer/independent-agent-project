using Cysharp.Threading.Tasks;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TMPro;
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
        private GameObject mSettingPanel;
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
        [Header("保存设置确认弹窗")]
        [SerializeField]
        private GameObject mSaveSettingMsgBox;
        [Header("无API Key弹窗")]
        [SerializeField]
        private GameObject mNoApiKeyMsgbox;
        [Header("退出游戏弹窗")]
        [SerializeField]
        private GameObject mQuitMsgbox;

        // ===== v0.23.0：API 配置面板（4 组 × 3 个 TMP_InputField = 12 个） =====
        [Header("API 配置：LLM Agent 子面板")]
        [SerializeField]
        private TMP_InputField mAgentBaseInput;
        [SerializeField]
        private TMP_InputField mAgentKeyInput;
        [SerializeField]
        private TMP_InputField mAgentModelInput;
        [Header("API 配置：LLM Memory 子面板")]
        [SerializeField]
        private TMP_InputField mMemoryBaseInput;
        [SerializeField]
        private TMP_InputField mMemoryKeyInput;
        [SerializeField]
        private TMP_InputField mMemoryModelInput;
        [Header("API 配置：Embedding 子面板")]
        [SerializeField]
        private TMP_InputField mEmbeddingBaseInput;
        [SerializeField]
        private TMP_InputField mEmbeddingKeyInput;
        [SerializeField]
        private TMP_InputField mEmbeddingModelInput;
        [Header("API 配置：Reranker 子面板")]
        [SerializeField]
        private TMP_InputField mRerankerBaseInput;
        [SerializeField]
        private TMP_InputField mRerankerKeyInput;
        [SerializeField]
        private TMP_InputField mRerankerModelInput;

        /// <summary>当前已加载的配置（进入配置面板时从文件读取，保存时写回）。</summary>
        private ApiConfig mCurrentConfig;

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
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
        }
        void Start()
        {
            // 初始状态：显示「按任意按钮」，隐藏其余面板
            ShowPressAnyButton();
            LoadConfigOnce();
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
                // 配置子面板：ESC 弹出保存确认（有变更才弹，无变更直接返回设置）
                if (Input.GetButtonDown("Menu"))
                {
                    TryLeaveSubPanel();
                }
            }
            else if (mSettingPanel != null && mSettingPanel.activeSelf)
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
            SetPanelActive(mSettingPanel, false);
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
            SetPanelActive(mSettingPanel, false);
            SetPanelActive(mLLMAgentPanel, false);
            SetPanelActive(mLLMMemoryPanel, false);
            SetPanelActive(mEmbeddingPanel, false);
            SetPanelActive(mRerankerPanel, false);
            LockInput();
        }

        /// <summary>切换到设置面板（关闭 4 个配置子面板，从任一子面板返回设置总览）。</summary>
        public void ShowSetting()
        {
            SetPanelActive(mPressAnyButtonPanel, false);
            SetPanelActive(mMainMenuPanel, false);
            SetPanelActive(mLLMAgentPanel, false);
            SetPanelActive(mLLMMemoryPanel, false);
            SetPanelActive(mEmbeddingPanel, false);
            SetPanelActive(mRerankerPanel, false);
            SetPanelActive(mSettingPanel, true);
            LockInput();
            // 返回设置总览：刷新回填（子面板若有未保存编辑，在此丢弃回文件值）
            RefreshInputsFromSetting();
        }

        /// <summary>打开「LLM Agent」配置子面板。</summary>
        public void OnClickLLMAgentSetting()
        {
            SetSubPanelActive(mLLMAgentPanel);
        }

        /// <summary>打开「LLM Memory」配置子面板。</summary>
        public void OnClickLLMMemorySetting()
        {
            SetSubPanelActive(mLLMMemoryPanel);
        }

        /// <summary>打开「Embedding」配置子面板。</summary>
        public void OnClickEmbeddingSetting()
        {
            SetSubPanelActive(mEmbeddingPanel);
        }

        /// <summary>打开「Reranker」配置子面板。</summary>
        public void OnClickRerankerSetting()
        {
            SetSubPanelActive(mRerankerPanel);
        }

        /// <summary>确认保存：收集文本框 → 写盘 → 关闭弹窗返回设置总览。</summary>
        public void OnConfirmSaveSetting()
        {
            mCurrentConfig = CollectInputsToApiConfig();
            ApiConfigStore.Save(mCurrentConfig);
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
            ShowSetting();
        }

        /// <summary>取消保存：关闭保存确认弹窗并返回设置总览（不写盘）。</summary>
        public void OnCancelSaveSetting()
        {
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
            ShowSetting();
        }

        private void SetSubPanelActive(GameObject subPanel)
        {
            SetPanelActive(mLLMAgentPanel, subPanel == mLLMAgentPanel);
            SetPanelActive(mLLMMemoryPanel, subPanel == mLLMMemoryPanel);
            SetPanelActive(mEmbeddingPanel, subPanel == mEmbeddingPanel);
            SetPanelActive(mRerankerPanel, subPanel == mRerankerPanel);
            SetPanelActive(mSettingPanel, false);
            LockInput();
            // 打开子面板前确保回填文件值
            RefreshInputsFromSetting();
        }

        private bool IsSubPanelActive()
        {
            return (mLLMAgentPanel != null && mLLMAgentPanel.activeSelf)
                || (mLLMMemoryPanel != null && mLLMMemoryPanel.activeSelf)
                || (mEmbeddingPanel != null && mEmbeddingPanel.activeSelf)
                || (mRerankerPanel != null && mRerankerPanel.activeSelf);
        }

        /// <summary>ESC 从子面板返回：有编辑 → 弹保存确认；无编辑 → 直接返回设置。</summary>
        private void TryLeaveSubPanel()
        {
            if (HasSettingChanged())
            {
                ShowSaveSettingMsgBox();
            }
            else
            {
                ShowSetting();
            }
        }

        private void ShowSaveSettingMsgBox()
        {
            if (mSaveSettingMsgBox != null)
            {
                mSaveSettingMsgBox.SetActive(true);
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

        // ===== v0.23.0：API 配置读取 / 回填 / 收集 / 变更检测 =====

        /// <summary>从 api_config.json 加载一次配置（幂等，供回填与入口校验）。</summary>
        private void LoadConfigOnce()
        {
            if (mCurrentConfig == null)
            {
                mCurrentConfig = ApiConfigStore.Load();
            }
        }

        /// <summary>把 mCurrentConfig 的值填入 12 个文本框。</summary>
        private void RefreshInputsFromSetting()
        {
            LoadConfigOnce();
            SetInput(mAgentBaseInput, mCurrentConfig.agentApiBase);
            SetInput(mAgentKeyInput, mCurrentConfig.agentApiKey);
            SetInput(mAgentModelInput, mCurrentConfig.agentModel);
            SetInput(mMemoryBaseInput, mCurrentConfig.memoryApiBase);
            SetInput(mMemoryKeyInput, mCurrentConfig.memoryApiKey);
            SetInput(mMemoryModelInput, mCurrentConfig.memoryModel);
            SetInput(mEmbeddingBaseInput, mCurrentConfig.embeddingApiBase);
            SetInput(mEmbeddingKeyInput, mCurrentConfig.embeddingApiKey);
            SetInput(mEmbeddingModelInput, mCurrentConfig.embeddingModel);
            SetInput(mRerankerBaseInput, mCurrentConfig.rerankerApiBase);
            SetInput(mRerankerKeyInput, mCurrentConfig.rerankerApiKey);
            SetInput(mRerankerModelInput, mCurrentConfig.rerankerModel);
        }

        /// <summary>从 12 个文本框收集值构造 ApiConfig。</summary>
        private ApiConfig CollectInputsToApiConfig()
        {
            return new ApiConfig
            {
                agentApiBase = GetInput(mAgentBaseInput),
                agentApiKey = GetInput(mAgentKeyInput),
                agentModel = GetInput(mAgentModelInput),
                memoryApiBase = GetInput(mMemoryBaseInput),
                memoryApiKey = GetInput(mMemoryKeyInput),
                memoryModel = GetInput(mMemoryModelInput),
                embeddingApiBase = GetInput(mEmbeddingBaseInput),
                embeddingApiKey = GetInput(mEmbeddingKeyInput),
                embeddingModel = GetInput(mEmbeddingModelInput),
                rerankerApiBase = GetInput(mRerankerBaseInput),
                rerankerApiKey = GetInput(mRerankerKeyInput),
                rerankerModel = GetInput(mRerankerModelInput),
            };
        }

        /// <summary>当前文本框内容是否与已加载配置不同（供退出时判断 dirty）。</summary>
        private bool HasSettingChanged()
        {
            if (mCurrentConfig == null)
            {
                return false;
            }
            return !string.Equals(GetInput(mAgentBaseInput), mCurrentConfig.agentApiBase)
                || !string.Equals(GetInput(mAgentKeyInput), mCurrentConfig.agentApiKey)
                || !string.Equals(GetInput(mAgentModelInput), mCurrentConfig.agentModel)
                || !string.Equals(GetInput(mMemoryBaseInput), mCurrentConfig.memoryApiBase)
                || !string.Equals(GetInput(mMemoryKeyInput), mCurrentConfig.memoryApiKey)
                || !string.Equals(GetInput(mMemoryModelInput), mCurrentConfig.memoryModel)
                || !string.Equals(GetInput(mEmbeddingBaseInput), mCurrentConfig.embeddingApiBase)
                || !string.Equals(GetInput(mEmbeddingKeyInput), mCurrentConfig.embeddingApiKey)
                || !string.Equals(GetInput(mEmbeddingModelInput), mCurrentConfig.embeddingModel)
                || !string.Equals(GetInput(mRerankerBaseInput), mCurrentConfig.rerankerApiBase)
                || !string.Equals(GetInput(mRerankerKeyInput), mCurrentConfig.rerankerApiKey)
                || !string.Equals(GetInput(mRerankerModelInput), mCurrentConfig.rerankerModel);
        }

        /// <summary>入口校验：配置完整才放行；否则弹「无 API Key」提示。</summary>
        private bool EnsureApiConfigReady()
        {
            LoadConfigOnce();
            if (mCurrentConfig != null && mCurrentConfig.IsComplete())
            {
                return true;
            }
            if (mNoApiKeyMsgbox != null)
            {
                mNoApiKeyMsgbox.SetActive(true);
                LockInput();
            }
            return false;
        }

        /// <summary>校验通过后，向 Python 发送 InitRequest 并等待初始化完成。</summary>
        private async UniTask<bool> SendInitAndWait()
        {
            try
            {
                await AgentServiceAsyncExtensions.InitAsync();
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UITitle] Python 初始化失败：{e.Message}");
                return false;
            }
        }

        public async void OnClickNewGame()
        {
            if (!EnsureApiConfigReady())
            {
                return;
            }
            if (!await SendInitAndWait())
            {
                return;
            }
            GameFlowManager.Instance.StartNewGame(mFirstLevelName, mAgentName, mAgentDesc).Forget(Debug.LogException);
        }

        public async void OnClickContinueGame()
        {
            if (!EnsureApiConfigReady())
            {
                return;
            }
            if (!await SendInitAndWait())
            {
                return;
            }
            GameFlowManager.Instance.ContinueGame().Forget(Debug.LogException);
        }

        public void OnClickSetting()
        {
            ShowSetting();
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

        // ===== 文本框辅助 =====

        private static void SetInput(TMP_InputField input, string value)
        {
            if (input != null)
            {
                input.text = value ?? "";
            }
        }

        private static string GetInput(TMP_InputField input)
        {
            return input != null ? input.text ?? "" : "";
        }
    }
}
