using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    public class InterruptAgentStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_interrupt_agent");

        private readonly string mStopReason;

        public InterruptAgentStep(string stopReason = "系统关闭")
        {
            mStopReason = stopReason;
        }

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.InterruptAgentAsync(this.mStopReason);
        }
    }
}