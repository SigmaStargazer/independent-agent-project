using FrameworkDesign;
using IndependentAgentProject;
using Services;
using ShootingEditor2D;
using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static TMPro.Examples.ObjectSpin;

namespace IndependentAgentProject
{
    public class AgentManager : MonoSingleton<AgentManager>, IController
    {
        //private List<Agent> mAgents = new List<Agent>();
        private Dictionary<string, AIPlayer> mAgents = new Dictionary<string, AIPlayer>();

        // Start is called before the first frame update
        void Start()
        {
            this.RegisterEvent<GameOverEvent>(e =>
            {
                AgentService.Instance.SendSceneStop();
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        void OnEnable()
        {
            AgentService.Instance.OnStopAction += this.StopAction;
            AgentService.Instance.OnObserve += this.Observe;
            AgentService.Instance.OnMonitorTarget += this.MonitorTarget;
            AgentService.Instance.OnGetMonitorRecords += this.GetMonitorRecords;
            AgentService.Instance.OnMoveAgent += this.Move;
            AgentService.Instance.OnFollowTarget += this.FollowTarget;
            AgentService.Instance.OnInteract += this.Interact;
            AgentService.Instance.OnSelect += this.Select;
            AgentService.Instance.OnInput += this.TextInput;

            AgentService.Instance.OnPlanActionSequence += this.PlanActionSequence;
            AgentService.Instance.OnStartActionSequence += this.StartActionSequence;
            AgentService.Instance.OnContinueActionSequence += this.ContinueActionSequence;
            AgentService.Instance.OnStopActionSequence += this.StopActionSequence;
        }

        void OnDisable()
        {
            AgentService.Instance.OnStopAction -= this.StopAction;
            AgentService.Instance.OnObserve -= this.Observe;
            AgentService.Instance.OnMonitorTarget -= this.MonitorTarget;
            AgentService.Instance.OnGetMonitorRecords -= this.GetMonitorRecords;
            AgentService.Instance.OnMoveAgent -= this.Move;
            AgentService.Instance.OnFollowTarget -= this.FollowTarget;
            AgentService.Instance.OnInteract -= this.Interact;
            AgentService.Instance.OnSelect -= this.Select;
            AgentService.Instance.OnInput -= this.TextInput;

            AgentService.Instance.OnPlanActionSequence -= this.PlanActionSequence;
            AgentService.Instance.OnStartActionSequence -= this.StartActionSequence;
            AgentService.Instance.OnContinueActionSequence -= this.ContinueActionSequence;
            AgentService.Instance.OnStopActionSequence -= this.StopActionSequence;
        }
        void OnDestroy()
        {
            mAgents.Clear();
        }

        #region 注册与注销逻辑

        public void Register(AIPlayer agent)
        {
            if (agent != null && !mAgents.ContainsKey(agent.Name))
            {
                mAgents.Add(agent.Name, agent);
            }
        }

        public void UnRegister(AIPlayer agent)
        {
            if (agent != null && mAgents.ContainsKey(agent.Name))
            {
                mAgents.Remove(agent.Name);
            }
        }

        #endregion

        #region 接收Agent指令逻辑
        private void StopAction(string agent, string requestId, string actionType)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.StopAction(requestId, actionType);
            }
        }
        private void Observe(string agent, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Observe(requestId);
            }
        }
        private void MonitorTarget(string agent, string requestId, int objectIndex)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.MonitorTarget(requestId, objectIndex);
            }
        }
        private void GetMonitorRecords(string agent, string requestId, int monitorIndex)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.GetMonitorRecords(requestId, monitorIndex);
            }
        }

        private void Move(string agent, bool isRight, float distance)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Move(isRight, distance);
            }
        }
        private void FollowTarget(string agent, string requestId, int objectIndex, float minDistance, float maxDistance)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.FollowTarget(requestId, objectIndex, minDistance, maxDistance);
            }
        }

        private void Interact(string agent, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Interact(requestId);
            }
        }

        private void Select(string agent, int selection, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Select(selection, requestId);
            }
        }

        private void TextInput(string agent, string inputText, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.TextInput(inputText, requestId);
            }
        }
        #endregion

        #region ActionSequence相关
        private void PlanActionSequence(string agent, List<ActionStep> actionSequence, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.PlanActionSequence(actionSequence, requestId);
            }
        }

        private void StartActionSequence(string agent, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.StartActionSequence(requestId);
            }
        }
        private void ContinueActionSequence(string agent, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.ContinueActionSequence(requestId);
            }
        }

        private void StopActionSequence(string agent, string requestId)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.StopActionSequence(requestId);
            }
        }
        #endregion

        public IArchitecture GetArchitecture()
        {
            return IndependentAgentProject.Instance;
        }
    }
}

