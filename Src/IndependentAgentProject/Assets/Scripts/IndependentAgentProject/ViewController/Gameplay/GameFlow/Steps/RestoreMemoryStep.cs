using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    public class RestoreMemoryStep : IFlowStep
    {
        public string DisplayName => "读取记忆";

        private readonly int slotId;

        public RestoreMemoryStep(int slotId)
        {
            this.slotId = slotId;
        }

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.RestoreMemoryAsync(slotId);
        }
    }
}