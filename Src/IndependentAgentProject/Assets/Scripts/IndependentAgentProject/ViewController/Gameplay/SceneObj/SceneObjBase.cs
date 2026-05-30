using FrameworkDesign;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class SceneObjBase : MonoBehaviour, IController
    {
        public abstract string Name { get; }
        public abstract string Desc { get; }

        [Header("范围方位配置")]
        [SerializeField]
        protected bool mUseRangeDirection = false;

        /// <summary>
        /// 用于计算范围方位的Collider
        /// 不填则默认使用自身Collider2D
        /// </summary>
        [SerializeField]
        protected Collider2D mRangeCollider;

        public bool UseRangeDirection => mUseRangeDirection;
        public Collider2D RangeCollider
        {
            get
            {
                if (mRangeCollider != null)
                    return mRangeCollider;
                return GetComponent<Collider2D>();
            }
        }

        /// <summary>
        /// 状态机
        /// </summary>
        protected Dictionary<string, FSMStateBase> mStates = new Dictionary<string, FSMStateBase>();

        protected FSMStateBase mCurState;

        public string StateName { get; protected set; }

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

        // 交互区域列表（在 Inspector 里拖拽，或 Awake 自动收集）
        [Header("交互区域（留空则使用自身Collider）")]
        [SerializeField] private List<InteractionZone> mInteractionZones = new List<InteractionZone>();

        protected virtual void Awake()
        {
            // 强制注入基础状态
            RegisterState(new IdleState());
            RegisterState(new MoveState());
            // 自动收集所有子对象上的 InteractionZone
            if (mInteractionZones.Count == 0)
                mInteractionZones.AddRange(GetComponentsInChildren<InteractionZone>());
        }

        protected virtual void Start()
        {
            // 默认进入Idle状态
            ChangeState("Idle");
        }

        protected virtual void Update()
        {
            // 判断是否有未完成的curActionCtx达到停止条件
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.Displacement = Mathf.Abs(transform.position.x - mCurActionRuntime.StartPostion.x);
                mCurActionRuntime.ActionTime += Time.deltaTime;
                // ========= 1. 错误终止优先 =========
                if (mCurActionRuntime.ErrorConditionFunc?.Invoke() == true)
                {
                    mCurActionRuntime.State = ActionState.Failed;
                    var finishedRuntime = mCurActionRuntime;
                    mCurActionRuntime = null;

                    ChangeState("Idle");
                    OnActionFinished(finishedRuntime);// 触发Hook
                    return;
                }
                // ========= 2. 正常完成 =========
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

            mCurState?.OnUpdate(this);
        }
        protected virtual void FixedUpdate()
        {
            mCurState?.OnFixedUpdate(this);
        }

        // 注册到SceneObjManager
        protected virtual void OnEnable()
        {
            if (SceneObjManager.Instance != null)
                SceneObjManager.Instance.Register(this);
        }

        protected virtual void OnDisable()
        {
            if (SceneObjManager.Instance != null)
                SceneObjManager.Instance.UnRegister(this);
        }

        /// <summary>
        /// 注册状态
        /// </summary>
        /// <param name="state"></param>
        protected void RegisterState(FSMStateBase state)
        {
            mStates[state.Name] = state;
        }

        public virtual void ChangeState(string stateName)
        {
            if (!mStates.TryGetValue(stateName, out var newState))
            {
                Debug.LogError($"State {stateName} not registered");
                return;
            }
            StateName = stateName;
            mCurState?.OnExit(this);
            mCurState = newState;
            mCurState.OnEnter(this);
        }

        public string GetStateName()
        {
            return mCurState?.Name ?? "Idle";
        }

        public void StopAction()
        {
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.State = ActionState.Aborted;
                mCurActionRuntime = null;
            }
            return;
        }

        #region 交互区域判断
        /// <summary>
        /// 输入角色的GameObject，输出该角色是否在任意交互区域内
        /// <param name="chara">角色的GameObject。用于判断该</param>
        /// <returns>bool，该角色是否在任意交互区域内</returns>
        /// </summary>
        public bool IsCharacterInAnyZone(GameObject chara)
        {
            if (mInteractionZones.Count == 0)
            {
                // 降级：使用自身 Collider
                var selfCol = GetComponent<Collider2D>();
                var charaCol = chara?.GetComponent<Collider2D>();
                if (selfCol == null || charaCol == null) return false;
                return charaCol.Distance(selfCol).isOverlapped;
            }
            foreach (var zone in mInteractionZones)
                if (zone.ContainsCharacter(chara)) return true;
            return false;
        }
        public float GetNearestZoneDistance(GameObject chara)
        {
            if (mInteractionZones.Count == 0)
            {
                var selfCol = GetComponent<Collider2D>();
                var charaCol = chara?.GetComponent<Collider2D>();
                if (selfCol == null || charaCol == null) return float.MaxValue;
                return Vector2.Distance(selfCol.bounds.center, charaCol.bounds.center);
            }

            float min = float.MaxValue;
            foreach (var zone in mInteractionZones)
                min = Mathf.Min(min, zone.DistanceTo(chara));
            return min;
        }
        /// <summary>
        /// 获取角色所在的具体区域标签（用于区分语义，如正面/背面）
        /// <param name="chara">角色的GameObject。用于获取角色与该重合的交互区域标签</param>
        /// <returns>string，角色所在具体区域标签</returns>
        /// </summary>
        public string GetActiveZoneTag(GameObject chara)
        {
            foreach (var zone in mInteractionZones)
                if (zone.ContainsCharacter(chara)) return zone.ZoneTag;
            return null;
        }
        #endregion 交互区域判断

        public IArchitecture GetArchitecture()
        {
            return IndependentAgentProject.Instance;
        }
    }

}
