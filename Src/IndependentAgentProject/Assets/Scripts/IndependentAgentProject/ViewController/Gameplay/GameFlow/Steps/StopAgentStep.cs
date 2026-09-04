using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    public class StopAgentStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_stop_agent");

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.StopSceneAsync();
        }
    }
}
