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
        public string DisplayName => "É¾³ý¾É¼ÇÒä";
        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.DeleteMemoryAsync();
        }
    }
}
