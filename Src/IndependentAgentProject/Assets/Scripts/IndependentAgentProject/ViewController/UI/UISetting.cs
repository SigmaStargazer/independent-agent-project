using Cysharp.Threading.Tasks;
using Services;
using TMPro;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.1：API 配置读写组件（挂载在场景 UIConfig 上）。
    /// 负责 12 个 TMP_InputField 的读取 / 回填 / 变更检测 / 保存 / 完整性校验 / API 可用性测试。
    /// 页面切换方法全部在 UITitle；本组件仅通过回调请求切换（不持有面板引用）。
    /// </summary>
    public class UISetting : MonoBehaviour
    {
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

        /// <summary>12 个输入框的统一集合（与 ApiConfig 字段顺序一致），供批量回填/收集/变更检测。</summary>
        private TMP_InputField[] mAllInputs;

        /// <summary>v0.23.1：测试进行中是否已被用户取消（MsgboxModelTesting 点「取消」置 true）。</summary>
        private bool mTestCancelled;

        // ===== v0.23.1 新增回调（由 UITitle 注入） =====
        /// <summary>开始 API 测试（参数：测试类型 llm/embedding/rerank）。UITitle 据此关 SaveApiKey、开 ModelTesting。</summary>
        public System.Action<string> OnStartApiTest { get; set; }

        /// <summary>API 测试完成（参数：success, errormsg）。UITitle 据此关 ModelTesting、开 Available/Unavailable。</summary>
        public System.Action<bool, string> OnApiTestFinished { get; set; }

        /// <summary>请求返回 PanelSetting（UITitle 注入，内部直接 ShowSetting()）。固定目标，无需来源层级。</summary>
        public System.Action OnRequestBackToSetting { get; set; }

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

        // ===== v0.23.1：文本框读写（供 UILLMAgent / UILLMMemory / UILLMRerank 复制按钮使用） =====

        /// <summary>读某组 Base 文本框。group: agent | memory | embedding | reranker。</summary>
        public string GetBase(string group)
        {
            return GetGroupInput(group, 0);
        }

        /// <summary>读某组 Key 文本框。group: agent | memory | embedding | reranker。</summary>
        public string GetKey(string group)
        {
            return GetGroupInput(group, 1);
        }

        /// <summary>读某组 Model 文本框。group: agent | memory | embedding | reranker。</summary>
        public string GetModel(string group)
        {
            return GetGroupInput(group, 2);
        }

        /// <summary>覆盖某组 3 个文本框。group: agent | memory | embedding | reranker。不写盘。</summary>
        public void SetGroup(string group, string baseV, string keyV, string modelV)
        {
            TMP_InputField[] fields = GetGroupFields(group);
            if (fields == null)
            {
                Debug.LogWarning($"[UISetting] SetGroup 未知配置组: {group}");
                return;
            }
            SetInput(fields[0], baseV);
            SetInput(fields[1], keyV);
            SetInput(fields[2], modelV);
        }

        // ===== v0.23.1：测试后保存流程 =====

        /// <summary>
        /// 【测试后保存】（MsgboxSaveApiKey.Btn3）：不写盘，仅用文本框当前值发起「当前面板模型」的 API 可用性测试。
        /// 测试通过与否由 MsgboxModelAvailable / MsgboxModelUnavailable 决定是否写盘。
        /// </summary>
        public async void OnConfirmTestConfig()
        {
            // 1. 不保存到 api_config.json——用文本框当前值测试，避免不可用配置覆盖原可用配置。
            //    注意：不要改 mCurrentConfig（dirty 检测基准）。测试值由 GetCurrentGroupConfig()
            //    从文本框实时读取；mCurrentConfig 保持为文件原值，点「继续配置」后 ESC 仍能
            //    正确检测到文本框与文件不一致并弹 MsgboxSaveApiKey。
            mTestCancelled = false;

            // 2. 请求 UITitle 进入「测试中」状态（关 MsgboxSaveApiKey、开 ModelTesting、锁输入）
            OnStartApiTest?.Invoke(CurrentTestCategory());

            // 3. 发起测试（异步，await；用户取消时丢弃结果）
            bool ok;
            string errmsg;
            try
            {
                var (cat, baseV, keyV, modelV) = GetCurrentGroupConfig();
                await AgentServiceAsyncExtensions.ApiTestAsync(cat, baseV, keyV, modelV);
                ok = true; errmsg = "";
            }
            catch (System.Exception e)
            {
                ok = false; errmsg = e.Message;
            }

            // 4. 通知 UITitle 测试完成（关 ModelTesting、开 Available/Unavailable）。
            //    用户已在 Testing 弹窗点「取消」时，丢弃结果：不弹结果弹窗，停留当前面板。
            if (!mTestCancelled)
            {
                OnApiTestFinished?.Invoke(ok, errmsg);
            }
        }

        /// <summary>
        /// 测试期间取消（MsgboxModelTesting.Btn1「取消」）：置取消标志，丢弃异步结果，停留当前面板。
        /// 由 UITitle 的 CloseModelTestingMsgBox() 调用。
        /// </summary>
        public void CancelApiTest()
        {
            mTestCancelled = true;
        }

        /// <summary>
        /// 【保存退出】（MsgboxModelAvailable.Btn3「保存退出」）：测试通过后此刻才保存并返回 PanelSetting。
        /// </summary>
        public void OnConfirmSaveAfterTest()
        {
            mCurrentConfig = CollectInputsToApiConfig();   // 从文本框收集（仍为当前面板那组）
            ApiConfigStore.Save(mCurrentConfig);           // 测试通过后才写盘
            RefreshInputsFromConfig();
            OnRequestBackToSetting?.Invoke();               // 固定返回 PanelSetting（UITitle 切换）
        }

        /// <summary>
        /// 【退出】（MsgboxSaveApiKey.Btn2「退出」 / MsgboxModelUnavailable.Btn2「退出」）：
        /// 不保存，固定返回 PanelSetting。因退出目标固定，无需来源层级记录。
        /// </summary>
        public void OnExitToSetting()
        {
            OnRequestBackToSetting?.Invoke();
        }

        /// <summary>
        /// 当前面板对应测试类型：LLMAgent/LLMMemory → llm；Embedding → embedding；Reranker → rerank。
        /// 按当前激活的子面板判断。
        /// </summary>
        private string CurrentTestCategory()
        {
            if (IsActive(mAgentBaseInput)) return "llm";
            if (IsActive(mMemoryBaseInput)) return "llm";
            if (IsActive(mEmbeddingBaseInput)) return "embedding";
            if (IsActive(mRerankerBaseInput)) return "rerank";
            return "llm";
        }

        /// <summary>
        /// 取当前面板那组文本框的 (测试类型, base, key, model)。测试类型与 CurrentTestCategory 一致。
        /// </summary>
        private (string cat, string baseV, string keyV, string modelV) GetCurrentGroupConfig()
        {
            if (IsActive(mAgentBaseInput)) return ("llm", GetInput(mAgentBaseInput), GetInput(mAgentKeyInput), GetInput(mAgentModelInput));
            if (IsActive(mMemoryBaseInput)) return ("llm", GetInput(mMemoryBaseInput), GetInput(mMemoryKeyInput), GetInput(mMemoryModelInput));
            if (IsActive(mEmbeddingBaseInput)) return ("embedding", GetInput(mEmbeddingBaseInput), GetInput(mEmbeddingKeyInput), GetInput(mEmbeddingModelInput));
            if (IsActive(mRerankerBaseInput)) return ("rerank", GetInput(mRerankerBaseInput), GetInput(mRerankerKeyInput), GetInput(mRerankerModelInput));
            return ("llm", "", "", "");
        }

        // ===== 内部辅助 =====

        /// <summary>输入框所在面板是否激活（用于判断当前处于哪个配置面板）。</summary>
        private static bool IsActive(TMP_InputField input)
        {
            return input != null && input.isActiveAndEnabled && input.gameObject.activeInHierarchy;
        }

        /// <summary>按 group 取 3 个输入框（Base/Key/Model）。未知 group 返回 null。</summary>
        private TMP_InputField[] GetGroupFields(string group)
        {
            switch (group)
            {
                case "agent":
                    return new[] { mAgentBaseInput, mAgentKeyInput, mAgentModelInput };
                case "memory":
                    return new[] { mMemoryBaseInput, mMemoryKeyInput, mMemoryModelInput };
                case "embedding":
                    return new[] { mEmbeddingBaseInput, mEmbeddingKeyInput, mEmbeddingModelInput };
                case "reranker":
                    return new[] { mRerankerBaseInput, mRerankerKeyInput, mRerankerModelInput };
                default:
                    return null;
            }
        }

        private string GetGroupInput(string group, int index)
        {
            TMP_InputField[] fields = GetGroupFields(group);
            if (fields == null)
            {
                Debug.LogWarning($"[UISetting] GetGroupInput 未知配置组: {group}");
                return "";
            }
            return GetInput(fields[index]);
        }

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
