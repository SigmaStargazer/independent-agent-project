using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class DeviceBase : MonoBehaviour
    {
        public string deviceName { get; protected set; }
        public string deviceDesc { get; protected set; }

        public abstract string Interact(GameObject chara);

        /// <summary>
        /// 状态机
        /// </summary>
        protected Dictionary<string, FSMStateBase> states =
            new Dictionary<string, FSMStateBase>();

        protected FSMStateBase curState;

        protected virtual void Awake()
        {
            // 强制注入基础状态
            AddState(new IdleState());
            AddState(new MoveState());

            curState = states["Idle"];
            curState.OnEnter(this);
        }

        protected virtual void Update()
        {
            curState?.OnUpdate(this);
        }

        protected void AddState(FSMStateBase state)
        {
            states[state.Name] = state;
        }

        public void ChangeState(string stateName)
        {
            if (!states.TryGetValue(stateName, out var newState))
            {
                Debug.LogError($"State {stateName} not registered");
                return;
            }

            curState?.OnExit(this);
            curState = newState;
            curState.OnEnter(this);
        }

        public string GetStateName()
        {
            return curState.Name;
        }
    }

}
