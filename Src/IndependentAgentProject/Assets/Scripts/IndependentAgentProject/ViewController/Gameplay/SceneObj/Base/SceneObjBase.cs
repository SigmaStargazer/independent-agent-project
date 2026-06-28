using FrameworkDesign;
using System;
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

        // 交互区域列表（在 Inspector 里拖拽，或 Awake 自动收集）
        [Header("交互区域（留空则使用自身Collider）")]
        [SerializeField] protected List<InteractionZone> mInteractionZones = new List<InteractionZone>();

        /// <summary>
        /// 状态机
        /// </summary>
        protected Dictionary<string, FSMStateBase> mStates = new Dictionary<string, FSMStateBase>();
        protected FSMStateBase mCurState;
        public string StateName { get; protected set; }
        public event Action<SceneObjBase, string, string> OnStateChanged;
        public event Action<SceneObjBase, string, string> OnObjectEnabled;
        public event Action<SceneObjBase, string, string> OnObjectDisabled;
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
            OnObjectEnabled?.Invoke(this, "Disappearance", "Appearance");
        }

        protected virtual void OnDisable()
        {
            OnObjectDisabled?.Invoke(this, StateName, "Disappearance");
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
            if (StateName == stateName)
                return;

            string oldStateName = StateName;
            if (!mStates.TryGetValue(stateName, out var newState))
            {
                Debug.LogError($"State {stateName} not registered");
                return;
            }
            StateName = stateName;
            mCurState?.OnExit(this);
            mCurState = newState;
            mCurState.OnEnter(this);

            OnStateChanged?.Invoke(this, oldStateName, stateName);
        }

        public string GetStateName()
        {
            return mCurState?.Name ?? "Idle";
        }

        /// <summary>
        /// 当前状态是否应被判定为「不可被检测/追击」（即 mCurState 实现 IUndetectableState）。
        /// 统一在 SceneObjBase 提供：子类一般不需覆写，由各自 FSMState 自己决定。
        /// </summary>
        public virtual bool IsUndetectable => mCurState is IUndetectableState;

        /// <summary>
        /// 当前状态是否「禁止主动移动」（即 mCurState 实现 IImmovableState）。
        /// 用于 HumanPlayer 的输入屏蔽、AIPlayer 的 Move / Follow 工具，
        /// 以及 ActionSequence 中 MoveAction / FollowAction 的统一拒绝位移。
        /// </summary>
        public virtual bool IsImmovable => mCurState is IImmovableState;

        /// <summary>
        /// 当前状态是否「免疫致死伤害与受伤型重生」（即 mCurState 实现 IInvulnerableState）。
        /// 用于 CharaBase.Die() 入口拦截、PlayerBase.ReturnToCheckPointByHurt 免疫判定。
        /// 中性的 ReturnToCheckPoint（调试 / 系统重置）不走该判定。
        /// </summary>
        public virtual bool IsInvulnerable => mCurState is IInvulnerableState;

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
