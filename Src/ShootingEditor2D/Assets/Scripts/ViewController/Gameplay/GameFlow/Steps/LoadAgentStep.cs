using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ShootingEditor2D
{
    public class LoadAgentStep : IFlowStep
    {
        public string DisplayName => "º”‘ÿAgent";

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.LoadAgentAsync();
        }
    }
}