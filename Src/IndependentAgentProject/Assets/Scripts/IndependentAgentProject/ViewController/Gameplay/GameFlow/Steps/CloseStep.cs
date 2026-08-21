using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>v0.23.0b：关闭Agent Step（回 Title Flow 内）。
    /// 关闭全部已初始化系统（Agent/LLM 缓存/时间/记忆/DB/Embedder），与 InitializeStep 对称。
    /// Python 侧 AgentLifecycle.leave_game() 幂等，未初始化时跳过资源关闭（但 Agent/时间清理始终执行）。</summary>
    public class CloseStep : IFlowStep
    {
        public string DisplayName => "安全Agent系统";

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.CloseAsync();
        }
    }
}
