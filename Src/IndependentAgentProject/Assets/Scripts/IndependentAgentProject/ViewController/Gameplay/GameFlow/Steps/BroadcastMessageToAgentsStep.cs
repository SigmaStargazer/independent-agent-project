using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class BroadcastMessageToAgentsStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_broadcast_message");

        private readonly string mMessage;

        public BroadcastMessageToAgentsStep(string message)
        {
            this.mMessage = message;
        }

        public UniTask Execute()
        {
            SceneObjManager.Instance.BroadcastMessageToAgents(mMessage);
            // 没有异步的步骤，直接返回即可UniTask.CompletedTask;
            return UniTask.CompletedTask;
        }
    }
}