using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class DeleteMemoryStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_delete_memory");
        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.DeleteMemoryAsync();
        }
    }
}
