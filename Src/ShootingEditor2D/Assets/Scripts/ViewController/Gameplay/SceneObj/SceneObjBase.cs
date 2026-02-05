using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class SceneObjBase : MonoBehaviour
    {
        public abstract string Name { get; }
        public abstract string Desc { get; }

        /// <summary>
        /// 状态机
        /// </summary>
        protected Dictionary<string, FSMStateBase> states = new Dictionary<string, FSMStateBase>();

        protected FSMStateBase curState;

        public string StateName => curState.Name;

        // Idle hooks
        public virtual void OnIdleEnter() { }
        public virtual void OnIdleUpdate() { }
        public virtual void OnIdleFixedUpdate() { }
        public virtual void OnIdleExit() { }

        // Move hooks
        public virtual void OnMoveEnter() { }
        public virtual void OnMoveUpdate() { }
        public virtual void OnMoveFixedUpdate() { }
        public virtual void OnMoveExit() { }

        /// <summary>
        /// Action
        /// </summary>
        /// 
        // Action的上下文
        protected ActionContext curActionCtx;

        protected virtual void OnActionFinished(ActionContext finishedCtx) { }

        protected virtual void Awake()
        {
            // 强制注入基础状态
            RegisterState(new IdleState());
            RegisterState(new MoveState());

            //curState = states["Idle"];
            //curState.OnEnter(this);
        }

        protected virtual void Start()
        {
            // 默认进入Idle状态
            ChangeState("Idle");
            //curState = states["Idle"];
            //curState.OnEnter(this);
        }

        protected virtual void Update()
        {
            if (curActionCtx != null)
            {
                curActionCtx.ActionTime += Time.deltaTime;

                // 触发结束条件，并清空curActionCtx
                if (curActionCtx.EndCondition?.Invoke() == true)
                {
                    var finishedCtx = curActionCtx;
                    curActionCtx = null;

                    ChangeState("Idle");
                    OnActionFinished(finishedCtx);// 触发Hook
                    return;
                }
            }

            curState?.OnUpdate(this);
        }
        protected virtual void FixedUpdate()
        {
            curState?.OnFixedUpdate(this);
        }

        protected void RegisterState(FSMStateBase state)
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
