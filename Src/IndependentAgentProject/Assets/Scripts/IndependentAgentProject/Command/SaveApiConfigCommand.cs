using FrameworkDesign;
using Services;

namespace IndependentAgentProject
{
    /// <summary>
    /// 保存 API 配置（v0.23.4 MVC 化）。
    /// View（UIModelConfig 测试通过后）把文本框收集的 ApiConfig 交给本 Command：
    /// 写入 Model（更新已保存值）→ ApiConfigStore 落盘。保持 api_config.json 格式/路径不变。
    /// </summary>
    public class SaveApiConfigCommand : AbstractCommand
    {
        private readonly ApiConfig mConfig;

        public SaveApiConfigCommand(ApiConfig config)
        {
            mConfig = config;
        }

        protected override void OnExecute()
        {
            if (mConfig == null)
            {
                return;
            }
            var model = this.GetModel<IApiConfigModel>();
            Set(model.AgentBase, mConfig.AGENT_API_BASE);
            Set(model.AgentKey, mConfig.AGENT_API_KEY);
            Set(model.AgentModel, mConfig.AGENT_MODEL);
            Set(model.MemoryBase, mConfig.MEMORY_API_BASE);
            Set(model.MemoryKey, mConfig.MEMORY_API_KEY);
            Set(model.MemoryModel, mConfig.MEMORY_MODEL);
            Set(model.EmbeddingBase, mConfig.EMBEDDING_API_BASE);
            Set(model.EmbeddingKey, mConfig.EMBEDDING_API_KEY);
            Set(model.EmbeddingModel, mConfig.EMBEDDING_MODEL);
            Set(model.RerankerBase, mConfig.RERANKER_API_BASE);
            Set(model.RerankerKey, mConfig.RERANKER_API_KEY);
            Set(model.RerankerModel, mConfig.RERANKER_MODEL);
            ApiConfigStore.Save(mConfig);
        }

        private static void Set(BindableProperty<string> prop, string value)
        {
            prop.Value = value ?? "";
        }
    }
}
