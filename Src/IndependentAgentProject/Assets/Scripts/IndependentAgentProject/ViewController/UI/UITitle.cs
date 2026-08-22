using Cysharp.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 标题页 UI（v0.23.1）：页面切换 + MsgboxSaveApiKey（测试后保存）+ 3 个 API 测试结果弹窗。
    /// API 配置的读取 / 回填 / 变更检测 / 保存 / 测试流程下沉到 UISetting（挂 UIConfig）。
    /// v0.23.1 起退出配置面板固定返回 PanelSetting，不再记录弹窗来源层级（SaveMsgFrom/UILevel 已移除）。
    /// </summary>
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
        [Header("保存设置确认弹窗（v0.23.1 起不再使用，保留供以后设置项使用）")]
        [SerializeField]
        private GameObject mSaveSettingMsgBox;
        [Header("无API Key弹窗")]
        [SerializeField]
        private GameObject mNoApiKeyMsgbox;
        [Header("退出游戏弹窗")]
        [SerializeField]
        private GameObject mQuitMsgbox;

        // ===== v0.23.1：测试后保存相关弹窗 =====
        [Header("API 配置保存确认弹窗（4 个模型配置 Panel 退出专用：取消/退出/测试后保存）")]
        [SerializeField]
        private GameObject mSaveApiKeyMsgBox;
        [Header("API 测试结果弹窗")]
        [SerializeField]
        private UIMsgBox mModelTestingMsgbox;       // 测试中（取消）
        [SerializeField]
        private UIMsgBox mModelAvailableMsgbox;      // 可用（继续配置/保存退出）
        [SerializeField]
        private UIMsgBox mModelUnavailableMsgbox;    // 不可用（继续配置/退出）

        [Header("API 配置读写组件（挂 UIConfig 上的 UISetting）")]
        [SerializeField]
        private UISetting mSetting;

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
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            if (mModelTestingMsgbox != null)
                mModelTestingMsgbox.gameObject.SetActive(false);
            if (mModelAvailableMsgbox != null)
                mModelAvailableMsgbox.gameObject.SetActive(false);
            if (mModelUnavailableMsgbox != null)
                mModelUnavailableMsgbox.gameObject.SetActive(false);

            // 注入回调：UISetting 只请求，切换/弹窗显隐由 UITitle 执行
            if (mSetting != null)
            {
                mSetting.OnStartApiTest = OnStartApiTest;
                mSetting.OnApiTestFinished = OnApiTestFinished;
                mSetting.OnRequestBackToSetting = ShowSetting;
            }
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

        /// <summary>
        /// 切换到「按任意按钮」面板。
        /// </summary>
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

        /// <summary>
        /// 切换到主菜单面板。
        /// </summary>
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

        /// <summary>
        /// 切换到设置面板（关闭 4 个配置子面板，从任一子面板返回设置总览）。
        /// </summary>
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
            if (mSetting != null)
                mSetting.RefreshInputsFromConfig();
        }

        /// <summary>
        /// 打开「LLM Agent」配置子面板。
        /// </summary>
        public void OnClickLLMAgentSetting()
        {
            SetSubPanelActive(mLLMAgentPanel);
        }

        /// <summary>
        /// 打开「LLM Memory」配置子面板。
        /// </summary>
        public void OnClickLLMMemorySetting()
        {
            SetSubPanelActive(mLLMMemoryPanel);
        }

        /// <summary>
        /// 打开「Embedding」配置子面板。
        /// </summary>
        public void OnClickEmbeddingSetting()
        {
            SetSubPanelActive(mEmbeddingPanel);
        }

        /// <summary>
        /// 打开「Reranker」配置子面板。
        /// </summary>
        public void OnClickRerankerSetting()
        {
            SetSubPanelActive(mRerankerPanel);
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
            if (mSetting != null)
                mSetting.RefreshInputsFromConfig();
        }

        private bool IsSubPanelActive()
        {
            return (mLLMAgentPanel != null && mLLMAgentPanel.activeSelf)
                || (mLLMMemoryPanel != null && mLLMMemoryPanel.activeSelf)
                || (mEmbeddingPanel != null && mEmbeddingPanel.activeSelf)
                || (mRerankerPanel != null && mRerankerPanel.activeSelf);
        }

        /// <summary>
        /// ESC 从子面板返回：有编辑 → 弹 MsgboxSaveApiKey；无编辑 → 直接返回设置。
        /// </summary>
        private void TryLeaveSubPanel()
        {
            bool changed = mSetting != null && mSetting.HasConfigChanged();
            if (changed)
            {
                ShowSaveApiKeyMsgBox();
            }
            else
            {
                ShowSetting();
            }
        }

        /// <summary>
        /// 弹出 MsgboxSaveApiKey（4 个模型配置 Panel 退出专用）。
        /// </summary>
        private void ShowSaveApiKeyMsgBox()
        {
            if (mSaveApiKeyMsgBox != null)
            {
                mSaveApiKeyMsgBox.SetActive(true);
                LockInput();
            }
        }

        // ===== MsgboxSaveApiKey 按钮（由用户在场景中绑定到 UITitle 公开方法） =====

        /// <summary>Btn1「取消」：仅关弹窗，停留当前子面板（不切换）。</summary>
        public void OnClickCloseMsgBox()
        {
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            LockInput();
        }

        /// <summary>Btn2「退出」：不保存，固定返回 PanelSetting。</summary>
        public void OnClickExitSaveApiKey()
        {
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            if (mSetting != null)
                mSetting.OnExitToSetting();
        }

        /// <summary>Btn3「测试后保存」：关 SaveApiKey，由 UISetting 发起测试。</summary>
        public void OnClickConfirmTestApiKey()
        {
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            if (mSetting != null)
                mSetting.OnConfirmTestConfig();
        }

        // ===== 测试流程回调（由 UISetting 注入到 Awake，勿手动绑定） =====

        /// <summary>开始测试：关 SaveApiKey、开 ModelTesting、锁输入。</summary>
        private void OnStartApiTest(string category)
        {
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            if (mModelTestingMsgbox != null)
                mModelTestingMsgbox.gameObject.SetActive(true);
            LockInput();
        }

        /// <summary>测试完成：关 ModelTesting，按结果开 Available / Unavailable。</summary>
        private void OnApiTestFinished(bool success, string errmsg)
        {
            if (mModelTestingMsgbox != null)
                mModelTestingMsgbox.gameObject.SetActive(false);
            if (success)
            {
                if (mModelAvailableMsgbox != null)
                    mModelAvailableMsgbox.gameObject.SetActive(true);
            }
            else
            {
                if (mModelUnavailableMsgbox != null)
                {
                    mModelUnavailableMsgbox.SetText("模型不可用：\n" + errmsg);
                    mModelUnavailableMsgbox.gameObject.SetActive(true);
                }
            }
            LockInput();
        }

        // ===== 三个结果 Msgbox 按钮（由用户在场景中绑定到 UITitle 公开方法） =====

        /// <summary>MsgboxModelTesting.Btn1「取消」：停止测试（丢弃异步结果），关弹窗，停留当前面板。</summary>
        public void OnClickCancelTestApiKey()
        {
            if (mSetting != null)
                mSetting.CancelApiTest();
            if (mModelTestingMsgbox != null)
                mModelTestingMsgbox.gameObject.SetActive(false);
            LockInput();
        }

        /// <summary>MsgboxModelAvailable.Btn1「继续配置」：关弹窗，留在当前 Panel。</summary>
        public void CloseAvailableContinue()
        {
            if (mModelAvailableMsgbox != null)
                mModelAvailableMsgbox.gameObject.SetActive(false);
            LockInput();
        }

        /// <summary>MsgboxModelAvailable.Btn2「保存退出」：此刻才保存配置并返回 PanelSetting。</summary>
        public void OnClickSaveApiKeyExit()
        {
            if (mModelAvailableMsgbox != null)
                mModelAvailableMsgbox.gameObject.SetActive(false);
            if (mSetting != null)
                mSetting.OnConfirmSaveAfterTest();
        }

        /// <summary>MsgboxModelUnavailable.Btn1「继续配置」：关弹窗，留在当前 Panel。</summary>
        public void CloseUnavailableContinue()
        {
            if (mModelUnavailableMsgbox != null)
                mModelUnavailableMsgbox.gameObject.SetActive(false);
            LockInput();
        }

        /// <summary>MsgboxModelUnavailable.Btn2「退出」：关弹窗，返回 PanelSetting（不保存）。</summary>
        public void OnClickExitUnavailable()
        {
            if (mModelUnavailableMsgbox != null)
                mModelUnavailableMsgbox.gameObject.SetActive(false);
            if (mSetting != null)
                mSetting.OnExitToSetting();
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

        // ===== 入口 =====

        public void OnClickNewGame()
        {
            if (mSetting != null && !mSetting.IsConfigReady())
            {
                if (mNoApiKeyMsgbox != null)
                {
                    mNoApiKeyMsgbox.SetActive(true);
                    LockInput();
                }
                return;
            }
            GameFlowManager.Instance.StartNewGame(mFirstLevelName, mAgentName, mAgentDesc).Forget(Debug.LogException);
        }

        public void OnClickContinueGame()
        {
            if (mSetting != null && !mSetting.IsConfigReady())
            {
                if (mNoApiKeyMsgbox != null)
                {
                    mNoApiKeyMsgbox.SetActive(true);
                    LockInput();
                }
                return;
            }
            GameFlowManager.Instance.ContinueGame().Forget(Debug.LogException);
        }

        public void OnClickSetting()
        {
            ShowSetting();
        }

        /// <summary>
        /// 退出游戏。构建版结束进程；编辑器下退出 Play 模式。
        /// </summary>
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
