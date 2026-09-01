using FrameworkDesign;
using Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IndependentAgentProject
{
    /// <summary>
    /// Tab 配置项（数据驱动）：Tab 按钮 + 对应内容区 + 文案 key。
    /// </summary>
    [System.Serializable]
    public class SettingsTabConfig
    {
        public Button tabButton;       // Tab 切换按钮（场景拖拽）
        public GameObject content;     // 对应内容区
        public UITextKey titleKey;     // 文案 key（枚举下拉选择，None 表示不赋值）；经 UITextProvider 取显示名
    }

    /// <summary>
    /// v0.23.4：设置页根容器脚本（挂 PanelSetting，唯一脚本）。
    /// 职责：数据驱动 Tab 切换（SettingsTabConfig 表）+ 子面板切换（4 个模型配置 Panel）+ 画面配置（显示模式/分辨率）。
    /// 画面配置 MVC 化：数据在 IGameSettingsModel（BindableProperty 驱动 UI 自动刷新），
    /// 修改经 Command（ChangeDisplayModeCommand / ChangeResolutionCommand），落盘经 SaveGameSettingsCommand。
    /// 模型配置数据逻辑在 UIModelConfig（挂 ContentModelConfig），测试回调由 UITitle 直接注入 UIModelConfig。
    /// PanelSetting 作为设置页根容器，配置期间保持激活（切换 Tab / 子面板只改内部节点显隐，不失活根节点）。
    /// </summary>
    public class UISetting : MonoBehaviour, IController
    {
        // ===== 子面板（4 个模型配置 Panel，PanelSetting 的子节点，与 PanelTab 同层互斥） =====
        [Header("配置子面板（PanelSetting 子节点，与 PanelTab 同层互斥）")]
        [SerializeField]
        private GameObject mLLMAgentPanel;
        [SerializeField]
        private GameObject mLLMMemoryPanel;
        [SerializeField]
        private GameObject mEmbeddingPanel;
        [SerializeField]
        private GameObject mRerankerPanel;
        [SerializeField]
        private GameObject mPanelTab;   // PanelTab（Tab 按钮 + 内容区容器）

        /// <summary>请求退出设置页（UITitle 注入：关闭 PanelSetting 回主菜单）。</summary>
        public System.Action OnRequestExit { get; set; }

        // ===== 模型配置数据组件引用（ContentModelConfig 上的 UIModelConfig，打开子面板前回填用） =====
        [Header("模型配置数据组件（ContentModelConfig 上的 UIModelConfig）")]
        [SerializeField]
        private UIModelConfig mModelConfig;

        // ===== 设置页 Tab（数据驱动，可扩展任意数量） =====
        [Header("设置页 Tab（数据驱动，可扩展任意数量）")]
        [SerializeField]
        private SettingsTabConfig[] mSettingsTabs;   // 顺序即 Tab 顺序

        private int mCurrentTabIndex;

        // ===== 画面配置（ContentDisplaySettings 内容区，由 UISetting 引用驱动） =====
        [Header("画面配置（ContentDisplaySettings 内容区）")]
        [SerializeField]
        private TMP_Text mDisplayModeText;
        [SerializeField]
        private Button mDisplayModeLeft;   // ◀
        [SerializeField]
        private Button mDisplayModeRight;  // ▶
        [SerializeField]
        private TMP_Text mResolutionText;
        [SerializeField]
        private Button mResolutionLeft;
        [SerializeField]
        private Button mResolutionRight;

        private static readonly FullScreenMode[] kModes =
        {
            FullScreenMode.Windowed,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.ExclusiveFullScreen,
        };

        private static readonly (int w, int h)[] kResolutions =
        {
            (1024, 768),   // 常见 4:3
            (1280, 720),
            (1920, 1080),  // 需求要求必须含 1920x1080
            (2560, 1440),
        };

        void Awake()
        {
            InitSettingsTabs();
            InitDisplaySettings();
            InitDisplayButtons();
            RegisterDisplayModelEvents();
        }

        // ===== 子面板切换（4 个模型配置 Panel，与 PanelTab 同层互斥） =====

        /// <summary>打开「LLM Agent」配置子面板。</summary>
        public void OnClickLLMAgentSetting()   { OpenSubPanel(mLLMAgentPanel); }
        /// <summary>打开「LLM Memory」配置子面板。</summary>
        public void OnClickLLMMemorySetting()  { OpenSubPanel(mLLMMemoryPanel); }
        /// <summary>打开「Embedding」配置子面板。</summary>
        public void OnClickEmbeddingSetting()  { OpenSubPanel(mEmbeddingPanel); }
        /// <summary>打开「Reranker」配置子面板。</summary>
        public void OnClickRerankerSetting()   { OpenSubPanel(mRerankerPanel); }

        /// <summary>
        /// 打开指定配置子面板：隐藏 PanelTab，显示对应子面板。
        /// 根节点 PanelSetting 保持激活（新架构下子面板是 PanelSetting 子节点，若失活父节点会连带失活子面板）。
        /// </summary>
        private void OpenSubPanel(GameObject subPanel)
        {
            SetActive(mLLMAgentPanel, subPanel == mLLMAgentPanel);
            SetActive(mLLMMemoryPanel, subPanel == mLLMMemoryPanel);
            SetActive(mEmbeddingPanel, subPanel == mEmbeddingPanel);
            SetActive(mRerankerPanel, subPanel == mRerankerPanel);
            SetActive(mPanelTab, false);   // 隐藏 Tab 界面（PanelSetting 保持激活）
            if (mModelConfig != null)
            {
                mModelConfig.RefreshInputsFromConfig();   // 打开子面板前确保回填文件值
            }
        }

        /// <summary>返回设置页主界面（隐藏全部子面板，显示 PanelTab）。</summary>
        public void BackToSettingTab()
        {
            SetActive(mLLMAgentPanel, false);
            SetActive(mLLMMemoryPanel, false);
            SetActive(mEmbeddingPanel, false);
            SetActive(mRerankerPanel, false);
            SetActive(mPanelTab, true);
        }

        /// <summary>当前是否有配置子面板激活（供 UITitle ESC 分发判断）。</summary>
        public bool IsSubPanelActive()
        {
            return (mLLMAgentPanel != null && mLLMAgentPanel.activeSelf)
                || (mLLMMemoryPanel != null && mLLMMemoryPanel.activeSelf)
                || (mEmbeddingPanel != null && mEmbeddingPanel.activeSelf)
                || (mRerankerPanel != null && mRerankerPanel.activeSelf);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null)
            {
                go.SetActive(active);
            }
        }

        // ===== Tab 切换（数据驱动） =====

        private void InitSettingsTabs()
        {
            mCurrentTabIndex = 0;
            if (mSettingsTabs == null)
            {
                return;
            }
            for (int i = 0; i < mSettingsTabs.Length; i++)
            {
                int idx = i;   // 闭包捕获
                var tab = mSettingsTabs[i];
                if (tab.tabButton != null)
                {
                    tab.tabButton.onClick.RemoveAllListeners();
                    tab.tabButton.onClick.AddListener(() => SelectTab(idx));
                }
                if (tab.content != null)
                {
                    tab.content.SetActive(i == 0);
                }
            }
            RefreshTabTitles();
        }

        /// <summary>切到指定 Tab（供 UI 内点击 / 外部复位默认调用）。</summary>
        public void SelectTab(int index)
        {
            if (mSettingsTabs == null || index < 0 || index >= mSettingsTabs.Length)
            {
                return;
            }
            mCurrentTabIndex = index;
            for (int i = 0; i < mSettingsTabs.Length; i++)
            {
                if (mSettingsTabs[i].content != null)
                {
                    mSettingsTabs[i].content.SetActive(i == index);
                }
            }
            RefreshTabTitles();
        }

        /// <summary>复位到第一个 Tab（UITitle.ShowSetting 打开设置页时调用）；同时返回设置页主界面（隐藏子面板）。</summary>
        public void ResetToDefaultTab()
        {
            BackToSettingTab();
            SelectTab(0);
        }

        private void RefreshTabTitles()
        {
            if (mSettingsTabs == null)
            {
                return;
            }
            // 为每个 Tab 按钮下的子 Text 按 titleKey 赋文案（None 表示不赋值，保持场景手动配置）
            for (int i = 0; i < mSettingsTabs.Length; i++)
            {
                var tab = mSettingsTabs[i];
                if (tab.tabButton != null && tab.titleKey != UITextKey.None)
                {
                    TMP_Text txt = tab.tabButton.GetComponentInChildren<TMP_Text>();
                    if (txt != null)
                    {
                        txt.text = UITextProvider.Get(tab.titleKey);
                    }
                }
            }
        }

        // ===== 画面配置（MVC：数据在 IGameSettingsModel，修改经 Command） =====

        /// <summary>启动/打开设置页时应用已加载画面设置（Model 在 OnInit 已从文件解析，此处只应用 + 刷新，不写盘）。</summary>
        public void InitDisplaySettings()
        {
            ApplyScreen();   // 应用已加载的分辨率/模式到 Screen
            RefreshDisplayUI();
        }

        /// <summary>绑定画面箭头按钮（onClick → 加减方法）。场景不手动绑 OnClick，由本组件在 Awake 统一绑定。</summary>
        private void InitDisplayButtons()
        {
            if (mDisplayModeLeft != null)
            {
                mDisplayModeLeft.onClick.RemoveAllListeners();
                mDisplayModeLeft.onClick.AddListener(OnModeLeft);
            }
            if (mDisplayModeRight != null)
            {
                mDisplayModeRight.onClick.RemoveAllListeners();
                mDisplayModeRight.onClick.AddListener(OnModeRight);
            }
            if (mResolutionLeft != null)
            {
                mResolutionLeft.onClick.RemoveAllListeners();
                mResolutionLeft.onClick.AddListener(OnResLeft);
            }
            if (mResolutionRight != null)
            {
                mResolutionRight.onClick.RemoveAllListeners();
                mResolutionRight.onClick.AddListener(OnResRight);
            }
        }

        /// <summary>订阅 Model 的 BindableProperty，值变化自动刷新 UI（不依赖手动调 RefreshDisplayUI）。</summary>
        private void RegisterDisplayModelEvents()
        {
            var model = this.GetModel<IGameSettingsModel>();
            model.DisplayModeIndex.RegisterOnValueChanged(_ => OnDisplayModelChanged())
                .UnRegisterWhenGameObjectDestroyed(gameObject);
            model.ResolutionIndex.RegisterOnValueChanged(_ => OnDisplayModelChanged())
                .UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        private void OnDisplayModelChanged()
        {
            ApplyScreen();
            RefreshDisplayUI();
        }

        public void OnModeLeft()  { SendModeDelta(-1); }
        public void OnModeRight() { SendModeDelta(1); }
        public void OnResLeft()   { SendResDelta(-1); }
        public void OnResRight()  { SendResDelta(1); }

        private void SendModeDelta(int delta)
        {
            var model = this.GetModel<IGameSettingsModel>();
            int next = model.DisplayModeIndex.Value + delta;
            if (next < 0 || next >= kModes.Length)
            {
                return;   // 越界：按钮 interactable 已禁用，这里再兜底
            }
            this.SendCommand(new ChangeDisplayModeCommand(delta));
        }

        private void SendResDelta(int delta)
        {
            var model = this.GetModel<IGameSettingsModel>();
            int next = model.ResolutionIndex.Value + delta;
            if (next < 0 || next >= kResolutions.Length)
            {
                return;
            }
            this.SendCommand(new ChangeResolutionCommand(delta));
        }

        private int ModeIndex => CurrentSettings.DisplayModeIndex;
        private int ResIndex => CurrentSettings.ResolutionIndex;

        /// <summary>画面设置只读数据（经 GetGameSettingsQuery 统一获取）。</summary>
        private GameSettingsSnapshot CurrentSettings => this.SendQuery(new GetGameSettingsQuery());

        /// <summary>把 Model 当前值应用到 Screen（分辨率 + 全屏模式）。</summary>
        private void ApplyScreen()
        {
            int mode = Mathf.Clamp(ModeIndex, 0, kModes.Length - 1);
            int res = Mathf.Clamp(ResIndex, 0, kResolutions.Length - 1);
            var (w, h) = kResolutions[res];
            Screen.fullScreenMode = kModes[mode];
            Screen.SetResolution(w, h, kModes[mode]);
        }

        private void RefreshDisplayUI()
        {
            int mode = Mathf.Clamp(ModeIndex, 0, kModes.Length - 1);
            int res = Mathf.Clamp(ResIndex, 0, kResolutions.Length - 1);
            if (mDisplayModeText != null)
            {
                mDisplayModeText.text = UITextProvider.Get("mode_" + ModeKey(mode));
            }
            if (mResolutionText != null)
            {
                mResolutionText.text = string.Format(UITextProvider.Get("resolution_format"), kResolutions[res].w, kResolutions[res].h);
            }
            if (mDisplayModeLeft != null)   mDisplayModeLeft.interactable = mode > 0;
            if (mDisplayModeRight != null)  mDisplayModeRight.interactable = mode < kModes.Length - 1;
            if (mResolutionLeft != null)    mResolutionLeft.interactable = res > 0;
            if (mResolutionRight != null)   mResolutionRight.interactable = res < kResolutions.Length - 1;
        }

        private string ModeKey(int idx)
        {
            switch (idx)
            {
                case 0: return "windowed";
                case 1: return "borderless";
                default: return "fullscreen";
            }
        }

        /// <summary>画面设置是否有未保存变更（经 GetGameSettingsQuery 统一获取，不依赖内容区是否激活）。</summary>
        public bool HasDisplaySettingsChanged()
        {
            return CurrentSettings.HasChanged;
        }

        /// <summary>保存当前画面设置（MsgboxSaveSetting「保存并退出」调用，经 Command 落盘）。</summary>
        public void SaveDisplaySettings()
        {
            this.SendCommand<SaveGameSettingsCommand>();
        }

        /// <summary>还原画面设置为文件已保存值（MsgboxSaveSetting「退出」调用，经 Command 写回 Model，订阅回调自动恢复实际分辨率与 UI）。</summary>
        public void RevertDisplaySettings()
        {
            this.SendCommand<RevertGameSettingsCommand>();
        }

        public IArchitecture GetArchitecture()
        {
            return IndependentAgentProject.Instance;
        }
    }
}
