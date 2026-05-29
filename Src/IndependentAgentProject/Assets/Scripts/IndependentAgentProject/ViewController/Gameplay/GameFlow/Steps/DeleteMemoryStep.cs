using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class CreateAgentStep : IFlowStep
    {
        public string DisplayName => "´´½¨Agent";

        private readonly string name;
        private readonly string desc;

        public CreateAgentStep(string name, string desc)
        {
            this.name = name;
            this.desc = desc;
        }

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.CreateAgentAsync(name, desc);
        }
    }
}
