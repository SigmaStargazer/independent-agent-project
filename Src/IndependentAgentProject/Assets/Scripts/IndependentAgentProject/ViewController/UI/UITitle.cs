using Cysharp.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 标题页 UI（v0.23.4）：页面切换 + MsgboxSaveApiKey（测试后保存）+ 3 个 API 测试结果弹窗
    /// + MsgboxEmptyApiKey（配置不完整提示）+ MsgboxSaveSetting（画面设置变更确认）。
    /// 模型配置数据（读取/回填/变更检测/保存/测试流程）在 UIModelConfig（挂 ContentModelConfig）；
    /// 设置页根容器 UISetting（挂 PanelSetting）管 Tab 切换（数据驱动）与画面配置（显示模式/分辨率）。
    /// v0.23.4 导航：配置子面板 ESC → 返回 PanelTab；PanelTab ESC → 检测画面变更（有则弹 MsgboxSaveSetting）后回主菜单。
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
        [Header("新游戏弹窗")]
        [SerializeField]
        private GameObject mNewGameWarmingMsgbox;
        [Header("保存设置确认弹窗")]
        [SerializeField]
        private GameObject mSaveSettingMsgBox;
        [Header("无API Key弹窗")]
        [SerializeField]
        private GameObject mNoApiKeyMsgbox;
        [Header("配置不完整提示弹窗")]
        [SerializeField]
        private GameObject mEmptyApiKeyMsgbox;
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

        [Header("模型配置数据组件（挂 ContentModelConfig 上的 UIModelConfig，v0.23.4 起）")]
        [SerializeField]
        private UIModelConfig mModelConfig;

        [Header("设置页根容器（PanelSetting 上的 UISetting，v0.23.4 起用于 Tab 复位 / 画面变更检测）")]
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
            if (mEmptyApiKeyMsgbox != null)
                mEmptyApiKeyMsgbox.SetActive(false);
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

            // 注入回调：UIModelConfig 只请求，切换/弹窗显隐由 UITitle 执行
            if (mModelConfig != null)
            {
                mModelConfig.OnStartApiTest = OnStartApiTest;
                mModelConfig.OnApiTestFinished = OnApiTestFinished;
                mModelConfig.OnRequestBackToSetting = ShowSetting;
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
                // 设置页（PanelTab）：ESC 先检测画面变更，有则弹 MsgboxSaveSetting，无变更返回主菜单
                if (Input.GetButtonDown("Menu"))
                {
                    TryLeaveSettingTab();
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
            LockInput();
        }

        /// <summary>
        /// 切换到设置面板（复位 Tab + 隐藏子面板 + 刷新回填；从任一子面板返回设置总览也走这里）。
        /// 子面板 / Tab 的内部显隐由 UISetting（mSetting）统一调度，UITitle 只负责页面级切换。
        /// </summary>
        public void ShowSetting()
        {
            SetPanelActive(mPressAnyButtonPanel, false);
            SetPanelActive(mMainMenuPanel, false);
            SetPanelActive(mSettingPanel, true);
            LockInput();
            // 复位到默认 Tab（隐藏全部配置子面板），并刷新回填（子面板若有未保存编辑，在此丢弃回文件值）
            if (mSetting != null)
                mSetting.ResetToDefaultTab();
            if (mModelConfig != null)
                mModelConfig.RefreshInputsFromConfig();
        }

        /// <summary>
        /// 打开「LLM Agent」配置子面板（内部显隐由 UISetting 调度）。
        /// </summary>
        public void OnClickLLMAgentSetting()
        {
            if (mSetting != null)
                mSetting.OnClickLLMAgentSetting();
            LockInput();
        }

        /// <summary>
        /// 打开「LLM Memory」配置子面板（内部显隐由 UISetting 调度）。
        /// </summary>
        public void OnClickLLMMemorySetting()
        {
            if (mSetting != null)
                mSetting.OnClickLLMMemorySetting();
            LockInput();
        }

        /// <summary>
        /// 打开「Embedding」配置子面板（内部显隐由 UISetting 调度）。
        /// </summary>
        public void OnClickEmbeddingSetting()
        {
            if (mSetting != null)
                mSetting.OnClickEmbeddingSetting();
            LockInput();
        }

        /// <summary>
        /// 打开「Reranker」配置子面板（内部显隐由 UISetting 调度）。
        /// </summary>
        public void OnClickRerankerSetting()
        {
            if (mSetting != null)
                mSetting.OnClickRerankerSetting();
            LockInput();
        }

        /// <summary>是否有配置子面板激活（委托给 UISetting，供 ESC 分发判断）。</summary>
        private bool IsSubPanelActive()
        {
            return mSetting != null && mSetting.IsSubPanelActive();
        }

        /// <summary>
        /// ESC 从子面板返回：先判空项（有空 → MsgboxEmptyApiKey）；有变更 → MsgboxSaveApiKey；无变更 → 返回设置。
        /// </summary>
        private void TryLeaveSubPanel()
        {
            bool changed = mModelConfig != null && mModelConfig.HasConfigChanged();
            if (!changed)
            {
                ShowSetting();
                return;
            }
            if (mModelConfig != null && mModelConfig.HasEmptyFieldInActivePanel())
            {
                ShowEmptyApiKeyMsgBox();   // 有空框 → 配置不完整提示
                return;
            }
            ShowSaveApiKeyMsgBox();        // 无空框且有变更 → 原有保存确认
        }

        /// <summary>
        /// 弹出 MsgboxEmptyApiKey（配置子面板有文本框为空时退出提示）。
        /// </summary>
        private void ShowEmptyApiKeyMsgBox()
        {
            if (mEmptyApiKeyMsgbox != null)
            {
                mEmptyApiKeyMsgbox.SetActive(true);
                LockInput();
            }
        }

        /// <summary>
        /// ESC 从设置页（PanelTab）返回：画面设置有变更 → 弹 MsgboxSaveSetting；无变更 → 直接返回主菜单。
        /// </summary>
        private void TryLeaveSettingTab()
        {
            if (mSetting != null && mSetting.HasDisplaySettingsChanged())
            {
                ShowSaveSettingMsgBox();
                return;
            }
            ShowMainMenu();
        }

        /// <summary>
        /// 弹出 MsgboxSaveSetting（画面设置变更确认）。
        /// </summary>
        private void ShowSaveSettingMsgBox()
        {
            if (mSaveSettingMsgBox != null)
            {
                mSaveSettingMsgBox.SetActive(true);
                LockInput();
            }
        }

        // ===== MsgboxEmptyApiKey 按钮（由用户在场景中绑定到 UITitle 公开方法） =====

        /// <summary>Btn1「继续配置」：关弹窗，停留当前子面板。</summary>
        public void OnClickEmptyContinue()
        {
            if (mEmptyApiKeyMsgbox != null)
                mEmptyApiKeyMsgbox.SetActive(false);
            LockInput();
        }

        /// <summary>Btn2「退出」：关弹窗，返回设置页主界面（不保存）。</summary>
        public void OnClickEmptyExit()
        {
            if (mEmptyApiKeyMsgbox != null)
                mEmptyApiKeyMsgbox.SetActive(false);
            if (mModelConfig != null)
                mModelConfig.OnExitToSetting();
        }

        // ===== MsgboxSaveSetting 按钮（由用户在场景中绑定到 UITitle 公开方法） =====

        /// <summary>Btn1「保存并退出」：保存画面设置，返回主菜单。</summary>
        public void OnClickSaveSettingAndExit()
        {
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
            if (mSetting != null)
                mSetting.SaveDisplaySettings();
            ShowMainMenu();
        }

        /// <summary>Btn2「退出」：不保存画面设置，还原为文件已保存值后返回主菜单。</summary>
        public void OnClickExitSetting()
        {
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
            if (mSetting != null)
                mSetting.RevertDisplaySettings();
            ShowMainMenu();
        }

        /// <summary>Btn3「取消」：仅关弹窗，留在设置页。</summary>
        public void OnClickCancelSaveSetting()
        {
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
            LockInput();
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

        /// <summary>Btn2「退出」：不保存，固定返回设置页主界面。</summary>
        public void OnClickExitSaveApiKey()
        {
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            if (mModelConfig != null)
                mModelConfig.OnExitToSetting();
        }

        /// <summary>Btn3「测试后保存」：关 SaveApiKey，由 UIModelConfig 发起测试。</summary>
        public void OnClickConfirmTestApiKey()
        {
            if (mSaveApiKeyMsgBox != null)
                mSaveApiKeyMsgBox.SetActive(false);
            if (mModelConfig != null)
                mModelConfig.OnConfirmTestConfig();
        }

        // ===== 测试流程回调（由 UIModelConfig 注入到 Awake，勿手动绑定） =====

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
                    // v0.23.5：前缀「模型不可用：」走文案表，errmsg 原样透传（用户已确认）；换行由代码拼接，Excel 值不含 \n
                    mModelUnavailableMsgbox.SetText(UITextProvider.Get("msg_model_unavailable_prefix") + "\n" + errmsg);
                    mModelUnavailableMsgbox.gameObject.SetActive(true);
                }
            }
            LockInput();
        }

        // ===== 三个结果 Msgbox 按钮（由用户在场景中绑定到 UITitle 公开方法） =====

        /// <summary>MsgboxModelTesting.Btn1「取消」：停止测试（丢弃异步结果），关弹窗，停留当前面板。</summary>
        public void OnClickCancelTestApiKey()
        {
            if (mModelConfig != null)
                mModelConfig.CancelApiTest();
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

        /// <summary>MsgboxModelAvailable.Btn2「保存退出」：此刻才保存配置并返回设置页主界面。</summary>
        public void OnClickSaveApiKeyExit()
        {
            if (mModelAvailableMsgbox != null)
                mModelAvailableMsgbox.gameObject.SetActive(false);
            if (mModelConfig != null)
                mModelConfig.OnConfirmSaveAfterTest();
        }

        /// <summary>MsgboxModelUnavailable.Btn1「继续配置」：关弹窗，留在当前 Panel。</summary>
        public void CloseUnavailableContinue()
        {
            if (mModelUnavailableMsgbox != null)
                mModelUnavailableMsgbox.gameObject.SetActive(false);
            LockInput();
        }

        /// <summary>MsgboxModelUnavailable.Btn2「退出」：关弹窗，返回设置页主界面（不保存）。</summary>
        public void OnClickExitUnavailable()
        {
            if (mModelUnavailableMsgbox != null)
                mModelUnavailableMsgbox.gameObject.SetActive(false);
            if (mModelConfig != null)
                mModelConfig.OnExitToSetting();
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
            if (mModelConfig != null && !mModelConfig.IsConfigReady())
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
            if (mModelConfig != null && !mModelConfig.IsConfigReady())
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
