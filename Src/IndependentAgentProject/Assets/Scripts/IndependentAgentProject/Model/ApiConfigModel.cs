using FrameworkDesign;
using Services;

namespace IndependentAgentProject
{
    /// <summary>
    /// API 配置数据模型（v0.23.4 MVC 化）。
    /// 纯数据：只存 12 个字段的「已保存值」。
    /// 配置解析（从 ApiConfigStore 读文件）在 OnInit 里完成（QFramework 惯例）；
    /// 修改经 Command（SaveApiConfigCommand），完整性/空判断经 Query（ApiConfigReadyQuery / ApiConfigEmptyQuery）。
    /// 保持 api_config.json 格式/路径不变（字段名与 Python config/api_config_loader.API_CONFIG_KEYS 一致）。
    /// 注：api_config.json 当前为明文存储；加密属后续版本（Unity+Python 两端协同），不在本版本范围。
    /// </summary>
    public interface IApiConfigModel : IModel
    {
        BindableProperty<string> AgentBase { get; }
        BindableProperty<string> AgentKey { get; }
        BindableProperty<string> AgentModel { get; }

        BindableProperty<string> MemoryBase { get; }
        BindableProperty<string> MemoryKey { get; }
        BindableProperty<string> MemoryModel { get; }

        BindableProperty<string> EmbeddingBase { get; }
        BindableProperty<string> EmbeddingKey { get; }
        BindableProperty<string> EmbeddingModel { get; }

        BindableProperty<string> RerankerBase { get; }
        BindableProperty<string> RerankerKey { get; }
        BindableProperty<string> RerankerModel { get; }
    }

    public class ApiConfigModel : AbstractModel, IApiConfigModel
    {
        public BindableProperty<string> AgentBase { get; } = new BindableProperty<string>();
        public BindableProperty<string> AgentKey { get; } = new BindableProperty<string>();
        public BindableProperty<string> AgentModel { get; } = new BindableProperty<string>();

        public BindableProperty<string> MemoryBase { get; } = new BindableProperty<string>();
        public BindableProperty<string> MemoryKey { get; } = new BindableProperty<string>();
        public BindableProperty<string> MemoryModel { get; } = new BindableProperty<string>();

        public BindableProperty<string> EmbeddingBase { get; } = new BindableProperty<string>();
        public BindableProperty<string> EmbeddingKey { get; } = new BindableProperty<string>();
        public BindableProperty<string> EmbeddingModel { get; } = new BindableProperty<string>();

        public BindableProperty<string> RerankerBase { get; } = new BindableProperty<string>();
        public BindableProperty<string> RerankerKey { get; } = new BindableProperty<string>();
        public BindableProperty<string> RerankerModel { get; } = new BindableProperty<string>();

        protected override void OnInit()
        {
            var c = ApiConfigStore.Load();
            Set(AgentBase, c.AGENT_API_BASE);
            Set(AgentKey, c.AGENT_API_KEY);
            Set(AgentModel, c.AGENT_MODEL);
            Set(MemoryBase, c.MEMORY_API_BASE);
            Set(MemoryKey, c.MEMORY_API_KEY);
            Set(MemoryModel, c.MEMORY_MODEL);
            Set(EmbeddingBase, c.EMBEDDING_API_BASE);
            Set(EmbeddingKey, c.EMBEDDING_API_KEY);
            Set(EmbeddingModel, c.EMBEDDING_MODEL);
            Set(RerankerBase, c.RERANKER_API_BASE);
            Set(RerankerKey, c.RERANKER_API_KEY);
            Set(RerankerModel, c.RERANKER_MODEL);
        }

        private static void Set(BindableProperty<string> prop, string value)
        {
            prop.Value = value ?? "";
        }
    }
}
