using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    public class LoadAgentStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_load_agent");

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.LoadAgentAsync();
        }
    }
}