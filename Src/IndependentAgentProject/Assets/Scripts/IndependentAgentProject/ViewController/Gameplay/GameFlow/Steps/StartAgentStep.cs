using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{ 
    public class StartAgentStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_start_agent");

        private readonly int mapId;

        public StartAgentStep(int mapId)
        {
            this.mapId = mapId;
        }

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.StartSceneAsync(this.mapId);
        }
    }
}
