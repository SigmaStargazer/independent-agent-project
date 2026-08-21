using Cysharp.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 标题页 UI（v0.23.0b 拆分后）：仅保留页面切换逻辑。
    /// API 配置的读取 / 回填 / 变更检测 / 保存 / 校验下沉到 UISetting（挂 UIConfig）。
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
        [Header("保存设置确认弹窗")]
        [SerializeField]
        private GameObject mSaveSettingMsgBox;
        [Header("无API Key弹窗")]
        [SerializeField]
        private GameObject mNoApiKeyMsgbox;
        [Header("退出游戏弹窗")]
        [SerializeField]
        private GameObject mQuitMsgbox;

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

            // 注入保存/退出后的「回上一层」回调：UISetting 只请求，切换由 UITitle 执行
            if (mSetting != null)
            {
                mSetting.OnRequestBack = OnRequestBackFromSaveMsg;
            }
        }

        /// <summary>
        /// 保存确认弹窗的保存/退出按钮触发：回退到打开弹窗的那一层。
        /// </summary>
        private void OnRequestBackFromSaveMsg(UISetting.UILevel fromLevel)
        {
            if (fromLevel == UISetting.UILevel.Setting)
            {
                ShowMainMenu();   // 设置 → 主菜单
            }
            else
            {
                ShowSetting();    // 子面板 → 设置
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
        /// ESC 从子面板返回：有编辑 → 弹保存确认；无编辑 → 直接返回设置。
        /// </summary>
        private void TryLeaveSubPanel()
        {
            bool changed = mSetting != null && mSetting.HasConfigChanged();
            if (changed)
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
                // 记录弹窗来源层级：保存/退出后回退到该层
                if (mSetting != null)
                    mSetting.SaveMsgFrom = UISetting.UILevel.SubPanel;
                mSaveSettingMsgBox.SetActive(true);
                LockInput();
            }
        }

        /// <summary>
        /// 保存确认弹窗的「取消」按钮（Btn1，ESC 也触发）：仅关弹窗，停留在当前子面板（不切换）。
        /// </summary>
        public void CloseSaveConfigMsgBox()
        {
            if (mSaveSettingMsgBox != null)
                mSaveSettingMsgBox.SetActive(false);
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
