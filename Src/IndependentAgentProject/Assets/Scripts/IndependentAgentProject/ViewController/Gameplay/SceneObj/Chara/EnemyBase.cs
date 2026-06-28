using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 巡逻型敌人。
    /// - 沿一组 <c>mPatrolPoints</c> 巡逻：Idle 等待 -> Move 到下一个点 -> Idle ...
    /// - 视野范围（mVisionZone）感知到 PlayerBase 后进入 Chase 状态追击。
    /// - 攻击范围（mAttackZone）接触到 PlayerBase 时调用 player.Die()。
    /// - 玩家从背刺范围（mBackstabZone，InteractionZone ZoneTag="back"）交互时进入 Stunned（永久击晕）。
    /// - Dead / Stunned 时 IsInteractable=false，从源头拒绝再次交互。
    /// - 不使用 CharaBase 的 Follow 状态（移除注册）。
    /// </summary>
    public class EnemyBase : CharaBase
    {
        public override string Name => "敌人";
        public override string Desc => "看起来在巡逻，靠近会被发现。";

        [Header("巡逻配置")]
        [SerializeField] private List<Transform> mPatrolPoints = new();
        [SerializeField] private float mPatrolSpeed = 2f;
        [SerializeField] private float mWaitTime = 1f;

        [Header("追人配置")]
        [SerializeField] private float mChaseSpeed = 4f;

        [Header("感知子物体")]
        [SerializeField][Tooltip("视野范围子物体：普通 GameObject，挂 Trigger Collider2D")]
        private GameObject mVisionZone;
        [SerializeField][Tooltip("攻击范围子物体：普通 GameObject，挂 Trigger Collider2D")]
        private GameObject mAttackZone;
        [SerializeField][Tooltip("背刺交互子物体：GameObject，挂 Trigger Collider2D + InteractionZone(ZoneTag=back)")]
        private InteractionZone mBackstabZone;

        private int mCurrentPatrolIndex = 0;
        private float mWaitTimer = 0f;
        private Transform mTargetPoint;
        private bool mIsReturningToPatrol = false;
        private PlayerBase mChaseTarget = null;

        /// <summary>Dead / Stunned 状态下不可被交互（不接受背刺）。</summary>
        public override bool IsInteractable => !(mCurState is StunnedState) && !IsDead;

        protected override void Awake()
        {
            base.Awake();
            RegisterState(new ChaseState());
            RegisterState(new StunnedState());
            mStates.Remove("Follow");

            if (mBackstabZone != null && !mInteractionZones.Contains(mBackstabZone))
                mInteractionZones.Add(mBackstabZone);

            if (mVisionZone != null)
                mVisionZone.AddComponent<EnemyZoneForwarder>().Init(this, EnemyZoneKind.Vision);
            if (mAttackZone != null)
                mAttackZone.AddComponent<EnemyZoneForwarder>().Init(this, EnemyZoneKind.Attack);
        }

        protected override void Start()
        {
            base.Start();
            if (mPatrolPoints.Count <= 1) return;
            mCurrentPatrolIndex = 0;
            SetNextPatrolTarget();
        }

        #region Idle 巡逻等待
        public override void OnIdleEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }
        public override void OnIdleUpdate()
        {
            if (mIsReturningToPatrol) return;
            if (mPatrolPoints.Count <= 1) return;
            mWaitTimer += Time.deltaTime;
            if (mWaitTimer >= mWaitTime)
            {
                mWaitTimer = 0;
                SetNextPatrolTarget();
            }
        }
        private void SetNextPatrolTarget()
        {
            if (mPatrolPoints.Count <= 1) return;
            mCurrentPatrolIndex = (mCurrentPatrolIndex + 1) % mPatrolPoints.Count;
            mTargetPoint = mPatrolPoints[mCurrentPatrolIndex];
            if (mTargetPoint != null)
                TurnBack((mTargetPoint.position - transform.position).x);
            ChangeState("Move");
        }
        #endregion

        #region Move 巡逻或返回路径点
        public override void OnMoveEnter()
        {
            if (mTargetPoint != null)
                TurnBack((mTargetPoint.position - transform.position).x);
        }
        public override void OnMoveFixedUpdate()
        {
            if (mTargetPoint == null)
            {
                ChangeState("Idle");
                return;
            }
            transform.position = Vector3.MoveTowards(
                transform.position, mTargetPoint.position,
                mPatrolSpeed * Time.fixedDeltaTime);
            if (Vector3.Distance(transform.position, mTargetPoint.position) < 0.02f)
            {
                transform.position = mTargetPoint.position;
                mIsReturningToPatrol = false;
                mTargetPoint = null;
                ChangeState("Idle");
            }
        }
        #endregion

        #region Chase 追击玩家
        public virtual void OnChaseEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
            mIsReturningToPatrol = false;
        }
        public virtual void OnChaseFixedUpdate()
        {
            if (mChaseTarget == null || mChaseTarget.IsDead || mChaseTarget.IsUndetectable)
            {
                mChaseTarget = null;
                ChangeState("Idle");
                MoveToNearestPatrolPoint();
                return;
            }
            Vector3 dir = (mChaseTarget.transform.position - transform.position).normalized;
            TurnBack(dir.x);
            transform.position = Vector3.MoveTowards(
                transform.position, mChaseTarget.transform.position,
                mChaseSpeed * Time.fixedDeltaTime);
        }
        public virtual void OnChaseExit()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
        }
        #endregion

        #region Stunned 被背刺永久击晕
        public virtual void OnStunnedEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
            mChaseTarget = null;
            mTargetPoint = null;
        }
        public virtual void OnStunnedUpdate() { }
        #endregion

        #region Trigger 子物体回调（由 EnemyZoneForwarder 调用）
        public void OnVisionEnter(Collider2D other)
        {
            if (StateName == "Stunned" || StateName == "Dead" || StateName == "Chase") return;
            PlayerBase player = other.GetComponentInParent<PlayerBase>();
            if (player != null && !player.IsDead && !player.IsUndetectable)
            {
                mChaseTarget = player;
                ChangeState("Chase");
            }
        }
        public void OnVisionExit(Collider2D other)
        {
            if (StateName != "Chase") return;
            PlayerBase player = other.GetComponentInParent<PlayerBase>();
            if (player == null || player != mChaseTarget) return;
            mChaseTarget = null;
            ChangeState("Idle");
            MoveToNearestPatrolPoint();
        }
        public void OnAttackEnter(Collider2D other)
        {
            if (StateName != "Chase") return;
            PlayerBase player = other.GetComponentInParent<PlayerBase>();
            if (player != null && !player.IsDead) player.Die();
        }
        #endregion

        #region 背刺交互
        public override (bool success, string result) Interact(GameObject chara)
        {
            string zone = GetActiveZoneTag(chara);
            if (zone == "back")
            {
                ChangeState("Stunned");
                return (true, "你成功背刺了敌人！");
            }
            return (false, "无法从正面或侧面攻击敌人。");
        }
        #endregion

        private void MoveToNearestPatrolPoint()
        {
            if (mPatrolPoints.Count == 0) return;
            mIsReturningToPatrol = true;
            Transform nearest = null;
            float minDist = float.MaxValue;
            foreach (var pt in mPatrolPoints)
            {
                if (pt == null) continue;
                float d = Vector3.Distance(transform.position, pt.position);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = pt;
                }
            }
            mTargetPoint = nearest;
            if (mTargetPoint != null)
                TurnBack((mTargetPoint.position - transform.position).x);
            ChangeState("Move");
        }

        public class ChaseState : FSMStateBase
        {
            public override string Name => "Chase";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseEnter(); }
            public override void OnFixedUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseFixedUpdate(); }
            public override void OnExit(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseExit(); }
        }

        public class StunnedState : FSMStateBase, IUndetectableState, IImmovableState
        {
            public override string Name => "Stunned";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnStunnedEnter(); }
            public override void OnUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnStunnedUpdate(); }
        }
    }
}
