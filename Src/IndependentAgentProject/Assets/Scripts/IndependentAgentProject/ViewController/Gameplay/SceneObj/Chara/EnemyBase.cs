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
        [SerializeField] private float mWaitTime = 5f;

        [Header("追人配置")]
        [SerializeField] private float mChaseSpeed = 4f;

        [Header("感知子物体")]
        [SerializeField][Tooltip("视野范围子物体：普通 GameObject，挂 Trigger Collider2D")]
        private GameObject mVisionZone;
        [SerializeField][Tooltip("攻击范围子物体：普通 GameObject，挂 Trigger Collider2D")]
        private GameObject mAttackZone;
        [SerializeField][Tooltip("背刺交互子物体：GameObject，挂 Trigger Collider2D + InteractionZone(ZoneTag=back)")]
        private InteractionZone mBackstabZone;

        // 抵达巡逻点的 X 距离阈值。比浮点抖动稍宽松，避免在目标附近反复抖动。
        private const float kArriveEpsilonX = 0.05f;

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

        #region Idle 巡逻等待
        public override void OnIdleEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
            mWaitTimer = 0f;
        }
        public override void OnIdleUpdate()
        {
            // 两种来源会进入 Idle：
            //  A) 正常巡逻停顿：等 mWaitTime 后切到下一个巡逻点（SetNextPatrolTarget）。
            //  B) Chase 追丢回归：进入 Idle 之前调用方已经把 mTargetPoint 设为最近巡逻点
            //     并把 mIsReturningToPatrol 置 true，这里等 mWaitTime 后直接切 Move，
            //     沿用已设的 mTargetPoint，不再调 SetNextPatrolTarget 覆盖目标。
            if (mIsReturningToPatrol)
            {
                mWaitTimer += Time.deltaTime;
                if (mWaitTimer >= mWaitTime)
                {
                    mWaitTimer = 0;
                    if (mTargetPoint != null)
                    {
                        TurnBack((mTargetPoint.position - transform.position).x);
                        ChangeState("Move");
                    }
                    else
                    {
                        // 防御：返回目标已被清空（罕见，如巡逻点为空），回退到正常巡逻节奏。
                        mIsReturningToPatrol = false;
                    }
                }
                return;
            }
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
            float dx = mTargetPoint.position.x - transform.position.x;
            if (Mathf.Abs(dx) < kArriveEpsilonX)
            {
                if (mRigidbody2D != null)
                    mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
                mIsReturningToPatrol = false;
                mTargetPoint = null;
                ChangeState("Idle");
                return;
            }
            float dir = Mathf.Sign(dx);
            TurnBack(dir);
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(dir * mPatrolSpeed, mRigidbody2D.velocity.y);
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
                // 追丢：切 Idle 即可。回最近巡逻点的目标设置统一在 OnChaseExit 里做。
                ChangeState("Idle");
                return;
            }
            float dx = mChaseTarget.transform.position.x - transform.position.x;
            float dir = Mathf.Sign(dx);
            TurnBack(dir);
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(dir * mChaseSpeed, mRigidbody2D.velocity.y);
        }
        public virtual void OnChaseExit()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
            mChaseTarget = null;
            // Chase 退出的统一收口：把目标设为最远巡逻点 + 标 mIsReturningToPatrol。
            // - 追丢（OnChaseFixedUpdate）/ 离开视野（OnVisionExit）都经过这里，自动覆盖。
            // - 走向 Stunned 时 OnStunnedEnter 会随后清掉 mTargetPoint / mIsReturningToPatrol，
            //   FSM 顺序「旧 OnExit → 新 OnEnter」保证终态正确。
            // - 选最远点是产品决策：Chase 通常在玩家附近终止，朝远端走相当于重新扫一段最长的巡逻路径，
            //   比立刻回最近点更接近"恢复巡逻"的视觉直觉。
            SetTargetToFarthestPatrolPoint();
        }
        #endregion

        #region Stunned 被背刺永久击晕
        public virtual void OnStunnedEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
            mChaseTarget = null;
            mTargetPoint = null;
            mIsReturningToPatrol = false;
        }
        public virtual void OnStunnedUpdate() { }
        #endregion

        #region Trigger 子物体回调（由 EnemyZoneForwarder 调用）
        public void OnVisionEnter(Collider2D other)
        {
            // 用 IsImmovable 涵盖 Stunned / Dead（以及未来任意 IImmovableState）；
            // Chase 自身不是 immovable，但需要单独去重避免重复 ChangeState。
            if (IsImmovable) return;
            if (StateName == "Chase") return;
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
            // 追丢：切 Idle 即可，回最近巡逻点的目标设置统一在 OnChaseExit 里做。
            ChangeState("Idle");
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
            if (zone == "Back")
            {
                ChangeState("Stunned");
                return (true, "你成功背刺了敌人！");
            }
            return (false, "无法从正面或侧面攻击敌人。");
        }
        #endregion

        /// <summary>
        /// 把 <see cref="mTargetPoint"/> 设为离当前位置**最远**的巡逻点，并把 <see cref="mIsReturningToPatrol"/>
        /// 置为 true（让接下来的 Idle 等待结束后直接走向该点，而不是按巡逻序列下一个点）。
        /// 选最远点的意图：Chase 通常在玩家附近终止，朝远点走相当于"重新扫一遍最长的一段巡逻路径"，
        /// 比立刻回到最近点更符合"恢复巡逻"的视觉直觉。
        /// 仅设置目标、不切换状态、不翻转面朝——面朝的翻转延迟到 <see cref="OnIdleUpdate"/> 真正切 Move 那一刻，
        /// 避免「追丢瞬间立刻扭头看巡逻点」的违和感。
        /// </summary>
        private void SetTargetToFarthestPatrolPoint()
        {
            if (mPatrolPoints.Count == 0)
            {
                mTargetPoint = null;
                mIsReturningToPatrol = false;
                return;
            }
            Transform farthest = null;
            float maxDist = -1f;
            foreach (var pt in mPatrolPoints)
            {
                if (pt == null) continue;
                float d = Vector3.Distance(transform.position, pt.position);
                if (d > maxDist)
                {
                    maxDist = d;
                    farthest = pt;
                }
            }
            mTargetPoint = farthest;
            mIsReturningToPatrol = mTargetPoint != null;
        }
    }
}
