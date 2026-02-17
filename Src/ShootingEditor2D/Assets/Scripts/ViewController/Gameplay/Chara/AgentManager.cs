using FrameworkDesign;
using Services;
using ShootingEditor2D;
using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class AgentManager : MonoSingleton<AgentManager>
    {
        //private List<Agent> mAgents = new List<Agent>();
        private Dictionary<string, Agent> mAgents = new Dictionary<string, Agent>();

        // Start is called before the first frame update
        void Start()
        {
            AgentService.Instance.OnObserve = this.Observe;
            AgentService.Instance.OnMoveAgent = this.Move;
            AgentService.Instance.OnInteract = this.Interact;
            AgentService.Instance.OnSelect = this.Select;
            AgentService.Instance.OnInput = this.TextInput;

            AgentService.Instance.OnPlanActionSequence = this.PlanActionSequence;
            AgentService.Instance.OnStartActionSequence = this.StartActionSequence;
            AgentService.Instance.OnCancelActionSequence = this.CancelActionSequence;
        }

        void Update()
        {

        }
        void OnDestroy()
        {
            mAgents.Clear();
        }

        #region 注册与注销逻辑

        public void Register(Agent agent)
        {
            if (agent != null && !mAgents.ContainsKey(agent.Name))
            {
                mAgents.Add(agent.Name, agent);
            }
        }

        public void UnRegister(Agent agent)
        {
            if (agent != null && mAgents.ContainsKey(agent.Name))
            {
                mAgents.Remove(agent.Name);
            }
        }

        #endregion

        #region 接收Agent指令逻辑
        private void Observe(string agent)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Observe();
            }
        }

        private void Move(string agent, bool isRight, float distance)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Move(isRight, distance);
            }
        }

        private void Interact(string agent)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Interact();
            }
        }

        private void Select(string agent, int selection)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.Select(selection);
            }
        }

        private void TextInput(string agent, string inputText)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.TextInput(inputText);
            }
        }
        #endregion

        #region ActionSequence相关
        private void PlanActionSequence(string agent, List<ActionStep> ActionSequence)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.PlanActionSequence(ActionSequence);
            }
        }

        private void StartActionSequence(string agent)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.StartActionSequence();
            }
        }

        private void CancelActionSequence(string agent)
        {
            if (mAgents.TryGetValue(agent, out var agentObj))
            {
                agentObj.CancelActionSequence();
            }
        }
        #endregion
    }
}

