using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
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

        public string StateName;

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

        // Action的上下文
        protected ActionRuntime mCurActionRuntime;

        protected virtual void OnActionFinished(ActionRuntime finishedActionRuntime) { }

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
            // 判断是否有未完成的curActionCtx达到停止条件
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.Displacement = Mathf.Abs(transform.position.x - mCurActionRuntime.StartPostion.x);
                mCurActionRuntime.ActionTime += Time.deltaTime;

                // 触发结束条件，并清空curActionCtx
                if (mCurActionRuntime.CompleteConditionFunc?.Invoke() == true)
                {
                    mCurActionRuntime.State = ActionState.Done;
                    var finishedRuntime = mCurActionRuntime;
                    mCurActionRuntime = null;

                    ChangeState("Idle");
                    OnActionFinished(finishedRuntime);// 触发Hook
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
            StateName = stateName;
            curState?.OnExit(this);
            curState = newState;
            curState.OnEnter(this);
        }

        public string GetStateName()
        {
            return curState.Name;
        }

        public void StopAction()
        {
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.State = ActionState.Aborted;
                mCurActionRuntime = null;
            }
            ChangeState("Idle");
            return;
        }
    }

}
