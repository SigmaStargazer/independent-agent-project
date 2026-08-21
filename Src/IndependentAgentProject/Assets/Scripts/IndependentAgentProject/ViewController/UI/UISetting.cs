using Services;
using TMPro;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.0b：API 配置读写组件（挂载在场景 UIConfig 上）。
    /// 从 UITitle 拆出，负责 12 个 TMP_InputField 的读取 / 回填 / 变更检测 / 保存 / 完整性校验。
    /// 页面切换方法全部在 UITitle；本组件仅通过 OnRequestBack 回调请求切换（不持有面板引用）。
    /// </summary>
    public class UISetting : MonoBehaviour
    {
        /// <summary>保存确认弹窗是从哪一层打开的（保存/退出后回退到该层）。由 UITitle 在弹窗前设置。</summary>
        public enum UILevel { SubPanel, Setting }

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

        [Header("保存设置确认弹窗（保存/退出按钮挂在 UISetting，仅控制本弹窗显隐）")]
        [SerializeField]
        private GameObject mSaveConfigMsgBox;

        /// <summary>当前已加载的配置（进入配置面板时从文件读取，保存时写回）。</summary>
        private ApiConfig mCurrentConfig;

        /// <summary>12 个输入框的统一集合（与 ApiConfig 字段顺序一致），供批量回填/收集/变更检测。</summary>
        private TMP_InputField[] mAllInputs;

        /// <summary>保存/退出后请求 UITitle 做「回上一层」切换（由 UITitle 在 Awake/Start 注入）。</summary>
        public System.Action<UILevel> OnRequestBack { get; set; }

        /// <summary>保存确认弹窗是从哪一层打开的。由 UITitle 在弹窗前设置。</summary>
        public UILevel SaveMsgFrom { get; set; } = UILevel.SubPanel;

        void Awake()
        {
            mAllInputs = new TMP_InputField[]
            {
                mAgentBaseInput, mAgentKeyInput, mAgentModelInput,
                mMemoryBaseInput, mMemoryKeyInput, mMemoryModelInput,
                mEmbeddingBaseInput, mEmbeddingKeyInput, mEmbeddingModelInput,
                mRerankerBaseInput, mRerankerKeyInput, mRerankerModelInput,
            };

            // TMP_InputField 默认「按 ESC 恢复原始文本」（撤销用户输入），
            // 与 UITitle 的 ESC 退出检测冲突：ESC 会先被输入框吞掉并还原输入，
            // 导致「只改一个值 ESC 不弹保存确认」且修改丢失。这里统一关闭。
            foreach (TMP_InputField input in mAllInputs)
            {
                if (input != null)
                {
                    input.restoreOriginalTextOnEscape = false;
                }
            }
        }

        /// <summary>
        /// 入口校验：配置完整才放行；否则返回 false（由 UITitle 决定弹提示）。
        /// </summary>
        public bool IsConfigReady()
        {
            LoadConfigOnce();
            return mCurrentConfig != null && mCurrentConfig.IsComplete();
        }

        /// <summary>
        /// 当前文本框内容是否与已加载配置不同（供退出子面板时判断 dirty）。
        /// </summary>
        public bool HasConfigChanged()
        {
            if (mCurrentConfig == null)
            {
                return false;
            }
            return !string.Equals(GetInput(mAgentBaseInput), mCurrentConfig.AGENT_API_BASE)
                || !string.Equals(GetInput(mAgentKeyInput), mCurrentConfig.AGENT_API_KEY)
                || !string.Equals(GetInput(mAgentModelInput), mCurrentConfig.AGENT_MODEL)
                || !string.Equals(GetInput(mMemoryBaseInput), mCurrentConfig.MEMORY_API_BASE)
                || !string.Equals(GetInput(mMemoryKeyInput), mCurrentConfig.MEMORY_API_KEY)
                || !string.Equals(GetInput(mMemoryModelInput), mCurrentConfig.MEMORY_MODEL)
                || !string.Equals(GetInput(mEmbeddingBaseInput), mCurrentConfig.EMBEDDING_API_BASE)
                || !string.Equals(GetInput(mEmbeddingKeyInput), mCurrentConfig.EMBEDDING_API_KEY)
                || !string.Equals(GetInput(mEmbeddingModelInput), mCurrentConfig.EMBEDDING_MODEL)
                || !string.Equals(GetInput(mRerankerBaseInput), mCurrentConfig.RERANKER_API_BASE)
                || !string.Equals(GetInput(mRerankerKeyInput), mCurrentConfig.RERANKER_API_KEY)
                || !string.Equals(GetInput(mRerankerModelInput), mCurrentConfig.RERANKER_MODEL);
        }

        /// <summary>
        /// 把文件中的配置回填到 12 个文本框（打开子面板 / 保存后调用）。
        /// 总是重新从文件读取，避免 mCurrentConfig 缓存陈旧导致回填过期值。
        /// </summary>
        public void RefreshInputsFromConfig()
        {
            mCurrentConfig = ApiConfigStore.Load();
            SetInput(mAgentBaseInput, mCurrentConfig.AGENT_API_BASE);
            SetInput(mAgentKeyInput, mCurrentConfig.AGENT_API_KEY);
            SetInput(mAgentModelInput, mCurrentConfig.AGENT_MODEL);
            SetInput(mMemoryBaseInput, mCurrentConfig.MEMORY_API_BASE);
            SetInput(mMemoryKeyInput, mCurrentConfig.MEMORY_API_KEY);
            SetInput(mMemoryModelInput, mCurrentConfig.MEMORY_MODEL);
            SetInput(mEmbeddingBaseInput, mCurrentConfig.EMBEDDING_API_BASE);
            SetInput(mEmbeddingKeyInput, mCurrentConfig.EMBEDDING_API_KEY);
            SetInput(mEmbeddingModelInput, mCurrentConfig.EMBEDDING_MODEL);
            SetInput(mRerankerBaseInput, mCurrentConfig.RERANKER_API_BASE);
            SetInput(mRerankerKeyInput, mCurrentConfig.RERANKER_API_KEY);
            SetInput(mRerankerModelInput, mCurrentConfig.RERANKER_MODEL);
        }

        /// <summary>
        /// 【保存并退出】（Btn3）：写盘 → 关弹窗 → 请求回上一层（由 UITitle 切换）。
        /// </summary>
        public void OnConfirmSaveConfig()
        {
            mCurrentConfig = CollectInputsToApiConfig();
            ApiConfigStore.Save(mCurrentConfig);
            RefreshInputsFromConfig();
            CloseAndRequestBack();
        }

        /// <summary>
        /// 【退出】（Btn2）：不保存 → 关弹窗 → 请求回上一层（由 UITitle 切换）。
        /// </summary>
        public void OnCancelSaveConfig()
        {
            CloseAndRequestBack();
        }

        /// <summary>
        /// 关闭保存确认弹窗并请求 UITitle 回退到打开弹窗的那一层。
        /// </summary>
        private void CloseAndRequestBack()
        {
            if (mSaveConfigMsgBox != null)
            {
                mSaveConfigMsgBox.SetActive(false);
            }
            OnRequestBack?.Invoke(SaveMsgFrom);
        }

        // ===== 内部辅助 =====

        /// <summary>
        /// 从 api_config.json 加载一次配置（幂等，供回填与入口校验）。
        /// </summary>
        private void LoadConfigOnce()
        {
            if (mCurrentConfig == null)
            {
                mCurrentConfig = ApiConfigStore.Load();
            }
        }

        /// <summary>
        /// 从 12 个文本框收集值构造 ApiConfig。
        /// </summary>
        private ApiConfig CollectInputsToApiConfig()
        {
            return new ApiConfig
            {
                AGENT_API_BASE = GetInput(mAgentBaseInput),
                AGENT_API_KEY = GetInput(mAgentKeyInput),
                AGENT_MODEL = GetInput(mAgentModelInput),
                MEMORY_API_BASE = GetInput(mMemoryBaseInput),
                MEMORY_API_KEY = GetInput(mMemoryKeyInput),
                MEMORY_MODEL = GetInput(mMemoryModelInput),
                EMBEDDING_API_BASE = GetInput(mEmbeddingBaseInput),
                EMBEDDING_API_KEY = GetInput(mEmbeddingKeyInput),
                EMBEDDING_MODEL = GetInput(mEmbeddingModelInput),
                RERANKER_API_BASE = GetInput(mRerankerBaseInput),
                RERANKER_API_KEY = GetInput(mRerankerKeyInput),
                RERANKER_MODEL = GetInput(mRerankerModelInput),
            };
        }

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
