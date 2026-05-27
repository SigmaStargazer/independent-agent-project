using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class ReturnToTitleFlow : IGameFlow
    {
        public bool ShowLoadingScreen => true;
        public FlowFailPolicy FailPolicy => FlowFailPolicy.ReturnTitle;
        public string TargetScene { get; }
        public IReadOnlyList<IFlowStep> Steps { get; }

        public ReturnToTitleFlow(string titleSceneName)
        {
            TargetScene = titleSceneName;
            Steps = new List<IFlowStep>()
            {
                new StopAgentStep(),

                new LoadSceneStep(titleSceneName),
            };
        }
    }
}