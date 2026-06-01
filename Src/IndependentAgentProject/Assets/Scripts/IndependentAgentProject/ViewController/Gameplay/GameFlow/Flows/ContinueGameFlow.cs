using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameFlow;

namespace IndependentAgentProject
{
    public class ContinueGameFlow : IGameFlow
    {
        public bool ShowLoadingScreen => true;
        public FlowFailPolicy FailPolicy => FlowFailPolicy.ReturnTitle;
        public string TargetScene { get; }
        public IReadOnlyList<IFlowStep> Steps { get; }

        public ContinueGameFlow()
        {
            SaveData data = SaveManager.Instance.Load();
            this.TargetScene = data.LevelName;
            Steps = new List<IFlowStep>
            {
                new StopAgentStep(),

                new RestoreMemoryStep(0),

                new LoadAgentStep(),

                new LoadSceneStep(this.TargetScene),

                new StartAgentStep(1),
            };
        }
    }
}
