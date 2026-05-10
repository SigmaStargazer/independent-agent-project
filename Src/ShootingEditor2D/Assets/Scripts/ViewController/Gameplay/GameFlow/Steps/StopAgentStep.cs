using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ShootingEditor2D
{
    public class StopAgentStep : IFlowStep
    {
        public string DisplayName => "停止Agent";

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.StopSceneAsync();
        }
    }
}
