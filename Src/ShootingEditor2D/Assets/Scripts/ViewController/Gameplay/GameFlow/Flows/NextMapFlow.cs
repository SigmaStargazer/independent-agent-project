using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class NextMapFlow : IGameFlow
    {
        public bool ShowLoadingScreen => true;
        public FlowFailPolicy FailPolicy => FlowFailPolicy.StayCurrentScene;
        public string TargetScene { get; }
        public IReadOnlyList<IFlowStep> Steps { get; }

        public NextMapFlow(string nextLevelName)
        {
            TargetScene = nextLevelName;
            string message = "系统管理员: 进入新场景。如有进行中的任务，请自行决定是否需要继续进行";
            Steps = new List<IFlowStep>()
            {
                new InterruptAgentStep("进入新场景"),

                new BackupMemoryStep(0),

                new SaveDataStep(this.TargetScene),

                new LoadSceneStep(this.TargetScene),

                new SaveDataStep(this.TargetScene),

                new BroadcastMessageToAgentsStep(message),

                new StartAgentStep(0),
            };
        }
    }
}

