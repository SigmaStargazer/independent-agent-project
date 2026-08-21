using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>v0.23.0b：初始化Agent Step（进游戏 Flow 开头）。
    /// 不只初始化记忆，还负责注入最新 API 配置并确保记忆/技能等系统就绪，故命名 InitializeStep。
    /// 幂等：Python 侧 AgentLifecycle.enter_game() 已初始化时直接返回。</summary>
    public class InitializeStep : IFlowStep
    {
        public string DisplayName => "初始化Agent系统";

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.InitAsync();
        }
    }
}
