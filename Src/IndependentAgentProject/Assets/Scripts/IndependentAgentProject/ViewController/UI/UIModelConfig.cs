using Cysharp.Threading.Tasks;
using FrameworkDesign;
using Services;
using TMPro;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.4：模型配置数据组件（挂 ContentModelConfig）。
    /// 负责 12 个 TMP_InputField 的读取 / 回填 / 变更检测 / 完整性校验 / API 可用性测试 / 复制按钮读写。
    /// 只负责「模型配置」Tab 的数据逻辑，不含 Tab 导航（导航在 UISetting，挂 PanelSetting）。
    /// MVC 化：已保存值在 IApiConfigModel（纯数据，OnInit 从文件解析），落盘经 SaveApiConfigCommand，
    /// 完整性/空判断经 ApiConfigReadyQuery / ApiConfigEmptyQuery。文本框是 View 编辑缓冲。
    /// 页面切换方法全部在 UITitle；本组件仅通过回调请求切换（不持有面板引用）。
    /// 注意：ContentModelConfig 会随 Tab 切换 SetActive(false)，但失活不影响外部引用调用公开方法
    /// （本组件公开方法纯读数据、不依赖 activeInHierarchy），故 UITitle / 复制按钮在隐藏时仍可调用。
    /// </summary>
    public class UIModelConfig : MonoBehaviour, IController
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

        /// <summary>12 个输入框的统一集合（顺序固定，供批量回填/收集/变更检测）。</summary>
        private TMP_InputField[] mAllInputs;

        /// <summary>v0.23.1：测试进行中是否已被用户取消（MsgboxModelTesting 点「取消」置 true）。</summary>
        private bool mTestCancelled;

        // ===== v0.23.1 新增回调（由 UITitle 注入） =====
        /// <summary>开始 API 测试（参数：测试类型 llm/embedding/rerank）。UITitle 据此关 SaveApiKey、开 ModelTesting。</summary>
        public System.Action<string> OnStartApiTest { get; set; }

        /// <summary>API 测试完成（参数：success, errormsg）。UITitle 据此关 ModelTesting、开 Available/Unavailable。</summary>
        public System.Action<bool, string> OnApiTestFinished { get; set; }

        /// <summary>请求返回设置页主界面（UITitle 注入，内部直接 ShowSetting()）。固定目标，无需来源层级。</summary>
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
        /// 基于 Model 的已保存值（Query），非文本框临时输入。
        /// </summary>
        public bool IsConfigReady()
        {
            return this.SendQuery(new ApiConfigReadyQuery());
        }

        /// <summary>
        /// 当前文本框内容是否与 Model 已保存值不同（供退出子面板时判断 dirty）。
        /// </summary>
        public bool HasConfigChanged()
        {
            var m = this.GetModel<IApiConfigModel>();
            return !string.Equals(GetInput(mAgentBaseInput), m.AgentBase.Value)
                || !string.Equals(GetInput(mAgentKeyInput), m.AgentKey.Value)
                || !string.Equals(GetInput(mAgentModelInput), m.AgentModel.Value)
                || !string.Equals(GetInput(mMemoryBaseInput), m.MemoryBase.Value)
                || !string.Equals(GetInput(mMemoryKeyInput), m.MemoryKey.Value)
                || !string.Equals(GetInput(mMemoryModelInput), m.MemoryModel.Value)
                || !string.Equals(GetInput(mEmbeddingBaseInput), m.EmbeddingBase.Value)
                || !string.Equals(GetInput(mEmbeddingKeyInput), m.EmbeddingKey.Value)
                || !string.Equals(GetInput(mEmbeddingModelInput), m.EmbeddingModel.Value)
                || !string.Equals(GetInput(mRerankerBaseInput), m.RerankerBase.Value)
                || !string.Equals(GetInput(mRerankerKeyInput), m.RerankerKey.Value)
                || !string.Equals(GetInput(mRerankerModelInput), m.RerankerModel.Value);
        }

        /// <summary>
        /// 把 Model 已保存值回填到 12 个文本框（打开子面板 / 保存后调用）。
        /// Model 是唯一权威源（OnInit 从文件解析 + SaveApiConfigCommand 更新），此处直接读 Model。
        /// </summary>
        public void RefreshInputsFromConfig()
        {
            var m = this.GetModel<IApiConfigModel>();
            SetInput(mAgentBaseInput, m.AgentBase.Value);
            SetInput(mAgentKeyInput, m.AgentKey.Value);
            SetInput(mAgentModelInput, m.AgentModel.Value);
            SetInput(mMemoryBaseInput, m.MemoryBase.Value);
            SetInput(mMemoryKeyInput, m.MemoryKey.Value);
            SetInput(mMemoryModelInput, m.MemoryModel.Value);
            SetInput(mEmbeddingBaseInput, m.EmbeddingBase.Value);
            SetInput(mEmbeddingKeyInput, m.EmbeddingKey.Value);
            SetInput(mEmbeddingModelInput, m.EmbeddingModel.Value);
            SetInput(mRerankerBaseInput, m.RerankerBase.Value);
            SetInput(mRerankerKeyInput, m.RerankerKey.Value);
            SetInput(mRerankerModelInput, m.RerankerModel.Value);
        }

        /// <summary>
        /// 当前激活的配置 Panel 三个文本框是否至少有一个为空（供 TryLeaveSubPanel 空项判定）。
        /// 配置子面板移入 PanelSetting（根容器配置期间保持激活）后，activeInHierarchy 判断始终可靠；
        /// 四个面板互斥保证至多命中一个。
        /// </summary>
        public bool HasEmptyFieldInActivePanel()
        {
            if (IsActive(mAgentBaseInput))    return HasEmpty(mAgentBaseInput, mAgentKeyInput, mAgentModelInput);
            if (IsActive(mMemoryBaseInput))   return HasEmpty(mMemoryBaseInput, mMemoryKeyInput, mMemoryModelInput);
            if (IsActive(mEmbeddingBaseInput))return HasEmpty(mEmbeddingBaseInput, mEmbeddingKeyInput, mEmbeddingModelInput);
            if (IsActive(mRerankerBaseInput)) return HasEmpty(mRerankerBaseInput, mRerankerKeyInput, mRerankerModelInput);
            return false;
        }

        private static bool HasEmpty(params TMP_InputField[] inputs)
        {
            foreach (var input in inputs)
            {
                if (input != null && string.IsNullOrWhiteSpace(input.text)) return true;
            }
            return false;
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
                Debug.LogWarning($"[UIModelConfig] SetGroup 未知配置组: {group}");
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
            //    注意：不要改 Model（dirty 检测基准是 Model 已保存值）。测试值由 GetCurrentGroupConfig()
            //    从文本框实时读取；Model 保持为文件原值，点「继续配置」后 ESC 仍能
            //    正确检测到文本框与 Model 不一致并弹 MsgboxSaveApiKey。
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
        /// 【保存退出】（MsgboxModelAvailable.Btn3「保存退出」）：测试通过后此刻才保存并返回设置页主界面。
        /// </summary>
        public void OnConfirmSaveAfterTest()
        {
            this.SendCommand(new SaveApiConfigCommand(CollectInputsToApiConfig()));   // 写 Model + 落盘
            RefreshInputsFromConfig();                                                // 回填（Model 值）
            OnRequestBackToSetting?.Invoke();                                         // 固定返回设置页主界面（UITitle 切换）
        }

        /// <summary>
        /// 【退出】（MsgboxSaveApiKey.Btn2「退出」 / MsgboxModelUnavailable.Btn2「退出」）：
        /// 不保存，固定返回设置页主界面。因退出目标固定，无需来源层级记录。
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
                Debug.LogWarning($"[UIModelConfig] GetGroupInput 未知配置组: {group}");
                return "";
            }
            return GetInput(fields[index]);
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

        public IArchitecture GetArchitecture()
        {
            return IndependentAgentProject.Instance;
        }
    }
}
