using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameFlow;

namespace IndependentAgentProject
{
    public class NewGameFlow : IGameFlow
    {
        public bool ShowLoadingScreen => true;
        public FlowFailPolicy FailPolicy => FlowFailPolicy.ReturnTitle;
        public string TargetScene { get; }
        public IReadOnlyList<IFlowStep> Steps { get; }

        public NewGameFlow(string firstLevelName, string agentName, string agentDesc)
        {
            this.TargetScene = firstLevelName;
            Steps = new List<IFlowStep>
            {
                new StopAgentStep(),

                new InitializeStep(),

                new DeleteMemoryStep(),

                new CreateAgentStep(agentName, agentDesc),

                new BackupMemoryStep(0),

                new SaveDataStep(this.TargetScene),

                new LoadAgentStep(),

                new LoadSceneStep(this.TargetScene),

                new StartAgentStep(1),
            };
        }
    }
}
