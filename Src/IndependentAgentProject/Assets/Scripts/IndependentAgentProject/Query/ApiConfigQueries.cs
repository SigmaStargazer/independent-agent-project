using FrameworkDesign;

namespace IndependentAgentProject
{
    /// <summary>
    /// API 配置是否 12 项全部非空（v0.23.4 MVC 化）。
    /// 替代原 ApiConfigModel.IsComplete()：判断逻辑外置为 Query，Model 保持纯数据。
    /// 供 UITitle 入口（开始新游戏/继续游戏）校验配置完整。
    /// </summary>
    public class ApiConfigReadyQuery : AbstractQuery<bool>
    {
        protected override bool OnDo()
        {
            var m = this.GetModel<IApiConfigModel>();
            return !string.IsNullOrWhiteSpace(m.AgentBase.Value)
                && !string.IsNullOrWhiteSpace(m.AgentKey.Value)
                && !string.IsNullOrWhiteSpace(m.AgentModel.Value)
                && !string.IsNullOrWhiteSpace(m.MemoryBase.Value)
                && !string.IsNullOrWhiteSpace(m.MemoryKey.Value)
                && !string.IsNullOrWhiteSpace(m.MemoryModel.Value)
                && !string.IsNullOrWhiteSpace(m.EmbeddingBase.Value)
                && !string.IsNullOrWhiteSpace(m.EmbeddingKey.Value)
                && !string.IsNullOrWhiteSpace(m.EmbeddingModel.Value)
                && !string.IsNullOrWhiteSpace(m.RerankerBase.Value)
                && !string.IsNullOrWhiteSpace(m.RerankerKey.Value)
                && !string.IsNullOrWhiteSpace(m.RerankerModel.Value);
        }
    }

    /// <summary>
    /// API 配置是否 12 项全部为空（v0.23.4 MVC 化）。
    /// 替代原 ApiConfigModel.IsEmpty()：判断逻辑外置为 Query，Model 保持纯数据。
    /// </summary>
    public class ApiConfigEmptyQuery : AbstractQuery<bool>
    {
        protected override bool OnDo()
        {
            var m = this.GetModel<IApiConfigModel>();
            return string.IsNullOrWhiteSpace(m.AgentBase.Value)
                && string.IsNullOrWhiteSpace(m.AgentKey.Value)
                && string.IsNullOrWhiteSpace(m.AgentModel.Value)
                && string.IsNullOrWhiteSpace(m.MemoryBase.Value)
                && string.IsNullOrWhiteSpace(m.MemoryKey.Value)
                && string.IsNullOrWhiteSpace(m.MemoryModel.Value)
                && string.IsNullOrWhiteSpace(m.EmbeddingBase.Value)
                && string.IsNullOrWhiteSpace(m.EmbeddingKey.Value)
                && string.IsNullOrWhiteSpace(m.EmbeddingModel.Value)
                && string.IsNullOrWhiteSpace(m.RerankerBase.Value)
                && string.IsNullOrWhiteSpace(m.RerankerKey.Value)
                && string.IsNullOrWhiteSpace(m.RerankerModel.Value);
        }
    }
}
