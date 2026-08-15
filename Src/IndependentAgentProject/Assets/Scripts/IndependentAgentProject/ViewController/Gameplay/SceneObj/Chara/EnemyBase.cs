using FrameworkDesign;
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
    /// - v0.22.1：新增 Alerted / Investigate / Inspect / Searching 四个状态，
    ///   订阅 <see cref="EnemyAnomalyEvent"/> 感知异常源（如碎玻璃）；
    ///   Chase / Searching 实现 <see cref="IBattleState"/> 表示战斗中不受异常打扰；
    ///   巡逻点抵达 Idle 瞬间应用 <see cref="PatrolPointConfig"/> 朝向偏好。
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

        [Header("异常调查配置")]
        [SerializeField][Tooltip("Alerted 状态持续时间（秒），到时后按仅警觉/完整调查分流。")]
        private float mAlertedSeconds = 1f;
        [SerializeField][Tooltip("Inspect 状态持续时间（秒），到时后回最远巡逻点。")]
        private float mInspectSeconds = 5f;
        [SerializeField][Tooltip("Inspect 期间每隔多久翻一次朝向。")]
        private float mInspectTurnInterval = 1.2f;
        [SerializeField][Tooltip("对同一 SourceObj 的冷却时间（秒），从调查链条结束时开始计。")]
        private float mSameSourceCooldown = 15f;

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
        private bool mArrivedFromPatrol = false;
        private PlayerBase mChaseTarget = null;

        // 异常事件状态
        private Vector2 mAnomalySource;
        private Vector2 mLostSightPos;
        private float mStateTimer = 0f;
        private float mInspectTurnTimer = 0f;

        // 仅警觉模式：异敌触发的事件只让本敌人 Alerted 一次然后回上一状态。
        private bool mAlertOnly = false;
        private string mPreAlertState = "Idle";

        // 当前正在响应的声源装置（Alerted/Investigate/Inspect 链条的目标源）。
        // 用于两处判定：
        // 1) 同源事件不打断（避免同一块玻璃反复重进 Alerted、重置计时）。
        // 2) 链条结束/中断时统一写冷却（不在 Alerted 进入时写，否则 Investigate+Inspect
        //    十几秒早把冷却耗完，无法起到"刚检查完不该立即再被同源吸引"的效果）。
        private SceneObjBase mCurrentSourceObj = null;

        // 每个声源装置对本敌人的独立冷却截止时间。
        private readonly Dictionary<SceneObjBase, float> mSourceCooldowns = new Dictionary<SceneObjBase, float>();

        /// <summary>Dead / Stunned 状态下不可被交互（不接受背刺）。</summary>
        public override bool IsInteractable => !(mCurState is StunnedState) && !IsDead;

        /// <summary>
        /// 当前状态是否「处于战斗中」（即 mCurState 实现 <see cref="IBattleState"/>）。
        /// 目前仅 EnemyBase 用于异常事件（<see cref="EnemyAnomalyEvent"/>）过滤：战斗中不响应异常吸引。
        /// 其他角色（Player 等）暂无此语义需求，因此不放到 SceneObjBase。
        /// </summary>
        public bool IsInBattle => mCurState is IBattleState;

        protected override void Awake()
        {
            base.Awake();
            RegisterState(new ChaseState());
            RegisterState(new SearchingState());
            RegisterState(new StunnedState());
            RegisterState(new AlertedState());
            RegisterState(new InvestigateState());
            RegisterState(new InspectState());
            mStates.Remove("Follow");

            if (mBackstabZone != null && !mInteractionZones.Contains(mBackstabZone))
                mInteractionZones.Add(mBackstabZone);

            if (mVisionZone != null)
                mVisionZone.AddComponent<EnemyZoneForwarder>().Init(this, EnemyZoneKind.Vision);
            if (mAttackZone != null)
                mAttackZone.AddComponent<EnemyZoneForwarder>().Init(this, EnemyZoneKind.Attack);

            this.RegisterEvent<EnemyAnomalyEvent>(OnEnemyAnomalyEventFired)
                .UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        protected override void Start()
        {
            base.Start();
            // 清洗 mPatrolPoints 中因 GameObject 被删除而残留的 null 占位。
            // Inspector 的 List<Transform> 不会自动移除被删 GameObject 的槽位,
            // 残留 null 会让 Count 误判为 >1,绕过单点站岗语义并引发 Idle->Move->Idle 瞬切循环。
            mPatrolPoints.RemoveAll(p => p == null);
            if (mPatrolPoints.Count <= 1) return;
            mCurrentPatrolIndex = 0;
            SetNextPatrolTarget();
        }
        public class ChaseState : FSMStateBase, IBattleState
        {
            public override string Name => "Chase";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseEnter(); }
            public override void OnFixedUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseFixedUpdate(); }
            public override void OnExit(SceneObjBase o) { if (o is EnemyBase e) e.OnChaseExit(); }
        }

        /// <summary>
        /// 追丢状态：Chase 中失去视野后进入，走向 mLostSightPos，到达后切 Inspect。
        /// 语义上属于战斗状态（IBattleState），不接受异常事件打扰。
        /// </summary>
        public class SearchingState : FSMStateBase, IBattleState
        {
            public override string Name => "Searching";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnSearchingEnter(); }
            public override void OnFixedUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnSearchingFixedUpdate(); }
        }

        public class StunnedState : FSMStateBase, IUndetectableState, IImmovableState
        {
            public override string Name => "Stunned";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnStunnedEnter(); }
            public override void OnUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnStunnedUpdate(); }
        }

        /// <summary>
        /// 警觉：面朝异常源，短暂停顿。到时后按 mAlertOnly 分流。
        /// 不实现 IUndetectable / IImmovable / IInvulnerable / IBattle，保留被视野发现、被背刺、被新异常打断的能力。
        /// </summary>
        public class AlertedState : FSMStateBase
        {
            public override string Name => "Alerted";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnAlertedEnter(); }
            public override void OnUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnAlertedUpdate(); }
        }

        /// <summary>
        /// 调查：走向异常源。到达后切 Inspect。
        /// </summary>
        public class InvestigateState : FSMStateBase
        {
            public override string Name => "Investigate";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnInvestigateEnter(); }
            public override void OnFixedUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnInvestigateFixedUpdate(); }
        }

        /// <summary>
        /// 张望：在异常源处原地停留、周期性翻朝向。计时到期后回最远巡逻点。
        /// </summary>
        public class InspectState : FSMStateBase
        {
            public override string Name => "Inspect";
            public override void OnEnter(SceneObjBase o) { if (o is EnemyBase e) e.OnInspectEnter(); }
            public override void OnUpdate(SceneObjBase o) { if (o is EnemyBase e) e.OnInspectUpdate(); }
        }

        #region Idle 巡逻等待
        public override void OnIdleEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
            mWaitTimer = 0f;
            if (mArrivedFromPatrol)
            {
                ApplyPatrolPointFacing();
                mArrivedFromPatrol = false;
            }
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
            // v0.22.21: 转向收敛到 CharaBase.OnMoveFixedUpdate（读 velocity.x），
            // OnMoveEnter 不再翻转——首帧朝向由同物理帧内随后执行的 OnMoveFixedUpdate 按速度修正。
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
                mArrivedFromPatrol = true;
                mTargetPoint = null;
                ChangeState("Idle");
                return;
            }
            float dir = Mathf.Sign(dx);
            // v0.22.21: 只写速度；转向由 CharaBase.OnMoveFixedUpdate 按本帧刚写入的 velocity.x 处理。
            mRigidbody2D.velocity = new Vector2(dir * mPatrolSpeed, mRigidbody2D.velocity.y);
            base.OnMoveFixedUpdate();
        }
        #endregion

        #region Chase 追击玩家
        public virtual void OnChaseEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
            mIsReturningToPatrol = false;
            mArrivedFromPatrol = false;
            // 战斗抢占了正在进行的异常调查链条：给旧当前源写冷却。
            WriteCooldownAndClearCurrentSource();
        }
        public virtual void OnChaseFixedUpdate()
        {
            if (mChaseTarget == null)
            {
                // 防御：Chase 中目标突然为空（罕见），直接回 Idle 恢复巡逻节奏。
                ChangeState("Idle");
                return;
            }
            if (mChaseTarget.IsDead || mChaseTarget.IsUndetectable)
            {
                // 追丢（玩家躲柜子 / 死亡）：记录最后位置，切 Searching。
                mLostSightPos = mChaseTarget.transform.position;
                ChangeState("Searching");
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
            // v0.22.1：Chase 退出不再直接设最远巡逻点。
            // 追丢路径（Searching → Inspect → 回最远点）由 Inspect 结束时统一处理；
            // 其他退出路径（直接 Idle 防御分支 / Stunned）由各自 Enter 负责。
        }
        #endregion

        #region Searching 追丢后走向最后已知位置
        public virtual void OnSearchingEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = Vector2.zero;
            TurnBack(mLostSightPos.x - transform.position.x);
        }
        public virtual void OnSearchingFixedUpdate()
        {
            float dx = mLostSightPos.x - transform.position.x;
            if (Mathf.Abs(dx) < kArriveEpsilonX)
            {
                if (mRigidbody2D != null)
                    mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
                // 到达最后已知位置：转入 Inspect 张望；Inspect 结束会回最远巡逻点。
                ChangeState("Inspect");
                return;
            }
            float dir = Mathf.Sign(dx);
            TurnBack(dir);
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(dir * mChaseSpeed, mRigidbody2D.velocity.y);
        }
        #endregion

        #region Alerted / Investigate / Inspect 异常调查
        public virtual void OnAlertedEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
            mChaseTarget = null;
            mArrivedFromPatrol = false;
            mStateTimer = 0f;
            TurnBack(mAnomalySource.x - transform.position.x);
        }
        public virtual void OnAlertedUpdate()
        {
            mStateTimer += Time.deltaTime;
            if (mStateTimer < mAlertedSeconds) return;

            if (mAlertOnly)
            {
                // 异敌触发路径：警觉结束回到 mPreAlertState，链条视为完成，写冷却。
                WriteCooldownAndClearCurrentSource();
                mAlertOnly = false;
                // 回 Idle 时复用巡逻抵达朝向逻辑：若当前巡逻点有 PatrolPointConfig，
                // 按配置恢复朝向（KeepCurrent 则保持警觉后朝向）；覆盖单点站岗与多点
                // 在巡逻点等待被打断两种情况。回 Move 时不触发，Move 自身按目标方向翻朝向。
                if (mPreAlertState == "Idle") mArrivedFromPatrol = true;
                ChangeState(mPreAlertState);
            }
            else
            {
                // 完整调查：进入 Investigate 走向异常源。
                ChangeState("Investigate");
            }
        }

        public virtual void OnInvestigateEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
            TurnBack(mAnomalySource.x - transform.position.x);
        }
        public virtual void OnInvestigateFixedUpdate()
        {
            float dx = mAnomalySource.x - transform.position.x;
            if (Mathf.Abs(dx) < kArriveEpsilonX)
            {
                if (mRigidbody2D != null)
                    mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
                ChangeState("Inspect");
                return;
            }
            float dir = Mathf.Sign(dx);
            TurnBack(dir);
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(dir * mPatrolSpeed, mRigidbody2D.velocity.y);
        }

        public virtual void OnInspectEnter()
        {
            if (mRigidbody2D != null)
                mRigidbody2D.velocity = new Vector2(0f, mRigidbody2D.velocity.y);
            mStateTimer = 0f;
            mInspectTurnTimer = 0f;
        }
        public virtual void OnInspectUpdate()
        {
            mStateTimer += Time.deltaTime;
            mInspectTurnTimer += Time.deltaTime;
            if (mInspectTurnTimer >= mInspectTurnInterval)
            {
                mInspectTurnTimer = 0f;
                TurnBack(-Mathf.Sign(transform.localScale.x));
            }
            if (mStateTimer >= mInspectSeconds)
            {
                // 完整调查链条结束：写冷却，回最远巡逻点。
                WriteCooldownAndClearCurrentSource();
                SetTargetToFarthestPatrolPoint();
                ChangeState("Idle");
            }
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
            mArrivedFromPatrol = false;
            mStateTimer = 0f;
            mInspectTurnTimer = 0f;
            // Stunned 抢占了异常调查链条：给旧当前源写冷却。
            WriteCooldownAndClearCurrentSource();
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
            // v0.22.1：离开视野切 Searching（走向最后已知位置），Searching 到达后进入 Inspect。
            mLostSightPos = player.transform.position;
            ChangeState("Searching");
        }
        public void OnAttackEnter(Collider2D other)
        {
            if (StateName != "Chase") return;
            PlayerBase player = other.GetComponentInParent<PlayerBase>();
            if (player != null && !player.IsDead) player.Die();
        }
        #endregion

        #region 背刺交互
        public override (bool success, string result, InteractAnimTag animTag) Interact(GameObject chara)
        {
            string zone = GetActiveZoneTag(chara);
            if (zone == "Back")
            {
                ChangeState("Stunned");
                return (true, "你成功背刺了敌人！", InteractAnimTag.Backstab);
            }
            return (false, "无法从正面或侧面攻击敌人。", InteractAnimTag.None);
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

        /// <summary>
        /// 敌人从巡逻抵达 <see cref="mCurrentPatrolIndex"/> 号巡逻点进入 Idle 瞬间的朝向偏好。
        /// 仅在 <see cref="mArrivedFromPatrol"/> 为 true 时被 <see cref="OnIdleEnter"/> 调用一次；
        /// 从 Chase / Inspect 等其他路径回归 Idle 时不生效（那些路径由自身逻辑控制朝向）。
        /// </summary>
        private void ApplyPatrolPointFacing()
        {
            if (mPatrolPoints.Count == 0) return;
            if (mCurrentPatrolIndex < 0 || mCurrentPatrolIndex >= mPatrolPoints.Count) return;
            var patrolPt = mPatrolPoints[mCurrentPatrolIndex];
            if (patrolPt == null) return;
            var cfg = patrolPt.GetComponent<PatrolPointConfig>();
            if (cfg == null) return;
            switch (cfg.Facing)
            {
                case PatrolFacing.KeepCurrent:
                    return;
                case PatrolFacing.Left:
                    TurnBack(-1f);
                    return;
                case PatrolFacing.Right:
                    TurnBack(1f);
                    return;
                case PatrolFacing.AutoByNextMove:
                    if (mPatrolPoints.Count <= 1) return;
                    int next = (mCurrentPatrolIndex + 1) % mPatrolPoints.Count;
                    var nextPt = mPatrolPoints[next];
                    if (nextPt == null) return;
                    TurnBack(nextPt.position.x - transform.position.x);
                    return;
            }
        }

        /// <summary>
        /// 链条结束/中断时统一给旧 <see cref="mCurrentSourceObj"/> 写冷却并清空。
        /// 冷却窗口从这一刻起计 <see cref="mSameSourceCooldown"/> 秒，避免刚检查完某声源就立即又被吸引。
        /// </summary>
        private void WriteCooldownAndClearCurrentSource()
        {
            if (mCurrentSourceObj != null)
            {
                // 同源冷却仅针对"其他敌人触发"的仅警觉链；玩家/装置触发的完整调查不写冷却。
                if (mAlertOnly)
                {
                    mSourceCooldowns[mCurrentSourceObj] = Time.time + mSameSourceCooldown;
                }
                mCurrentSourceObj = null;
            }
        }

        #region 异常事件订阅入口

        /// <summary>
        /// QFramework 事件回调：三层前置过滤——距离过滤、当前源不打断、同源冷却。
        /// 通过后转交 <see cref="OnHearAnomaly"/> 走 Triggerer 分流与状态更新。
        /// </summary>
        private void OnEnemyAnomalyEventFired(EnemyAnomalyEvent evt)
        {
            if (Vector2.Distance(transform.position, evt.SourcePos) > evt.Radius) return;
            if (evt.SourceObj != null && evt.SourceObj == mCurrentSourceObj) return;
            // 同源冷却仅对"其他敌人触发"的事件生效；玩家/装置触发的事件跳过冷却检查。
            bool eventIsAlertOnly = evt.Triggerer is EnemyBase && evt.Triggerer != this;
            if (eventIsAlertOnly
                && evt.SourceObj != null
                && mSourceCooldowns.TryGetValue(evt.SourceObj, out float endTime)
                && Time.time < endTime)
            {
                return;
            }
            OnHearAnomaly(evt.SourcePos, evt.Triggerer, evt.SourceObj);
        }

        /// <summary>
        /// 感知到异常事件后的核心分流：按 Triggerer / 当前状态 / 是否仅警觉，
        /// 决定是否覆盖 <see cref="mAnomalySource"/> 与 <see cref="mCurrentSourceObj"/>，
        /// 并强制重进 Alerted。详细规则见 solution.md §3.5.4 与 FR-2.4 表格。
        /// </summary>
        public void OnHearAnomaly(Vector2 sourcePos, SceneObjBase triggerer, SceneObjBase sourceObj)
        {
            if (triggerer == this) return;
            if (IsImmovable || IsDead) return;
            if (IsInBattle) return;

            bool eventIsAlertOnly = triggerer is EnemyBase && triggerer != this;
            bool enteringFromIdleOrMove = (StateName == "Idle" || StateName == "Move");

            // ============================================================
            // 分支 A：首次进入调查链（敌人当前在 Idle / Move 巡逻中，未在调查任何异常）
            // 场景：玩家或异敌踩到玻璃，敌人从巡逻状态被吸引。
            // 行为：全新赋值，记录出发状态 + 设置仅警觉标志 + 记录异常源。
            //   - 玩家触发：mAlertOnly=false，后续走完整调查（Alerted->Investigate->Inspect）。
            //   - 异敌触发：mAlertOnly=true，后续走仅警觉（Alerted->回 mPreAlertState）。
            // 无旧源需处理（之前不在调查链中）。
            // ============================================================
            if (enteringFromIdleOrMove)
            {
                mPreAlertState = StateName;
                mAlertOnly = eventIsAlertOnly;
                mAnomalySource = sourcePos;
                mCurrentSourceObj = sourceObj;
            }
            // ============================================================
            // 分支 B：完整调查被异敌打断，但保留原调查目标（空分支）
            // 前置条件：!mAlertOnly（当前链是玩家触发的完整调查）+ eventIsAlertOnly（新事件是异敌触发）
            // 场景：敌人正在调查玩家踩的玻璃 X，此时另一个敌人踩了玻璃 Y。
            // 决策：玩家完整调查优先级 > 异敌仅警觉，异敌事件只触发一次"分心警觉"，
            //   不接管调查目标。因此此处不修改任何字段：
            //   - 不替换 mAnomalySource（仍指向 X）
            //   - 不替换 mCurrentSourceObj（仍为 X）
            //   - 不改 mAlertOnly（仍为 false，继续完整调查）
            //   - 不写旧源冷却（没有替换源）
            // 后续 ForceReenterAlerted() 会让敌人面朝原 X 方向做一次警觉反应，然后继续 Investigate X。
            // 此分支体为空是"故意不修改"的语义，不是遗漏。
            // ============================================================
            else if (!mAlertOnly && eventIsAlertOnly)
            {
            }
            // ============================================================
            // 分支 C：替换当前调查源（已在调查链中，且不属于分支 B）
            // 覆盖三种子情况：
            //   B2：玩家完整调查中 + 玩家又触发新源（如玩家踩 X 调查中又踩 Y）-> 新完整调查接管。
            //   C1：异敌仅警觉中 + 玩家触发新源（异敌踩 X 仅警觉中，玩家踩 Y）-> 升级为完整调查。
            //   C2：异敌仅警觉中 + 另一异敌触发新源（异敌踩 X 仅警觉中，另一异敌踩 Y）-> 切换仅警觉目标。
            // 行为：旧源若与新源不同，按旧链是否异敌触发决定写不写冷却；然后替换为新源。
            // ============================================================
            else
            {
                if (mCurrentSourceObj != null && mCurrentSourceObj != sourceObj)
                {
                    // 旧链是异敌仅警觉(mAlertOnly==true)才写冷却；玩家完整调查链被新源替换时不写。
                    if (mAlertOnly)
                    {
                        mSourceCooldowns[mCurrentSourceObj] = Time.time + mSameSourceCooldown;
                    }
                }
                if (!eventIsAlertOnly) mAlertOnly = false;
                mAnomalySource = sourcePos;
                mCurrentSourceObj = sourceObj;
            }

            ForceReenterAlerted();
        }

        /// <summary>
        /// 强制切到 Alerted，重跑 <see cref="OnAlertedEnter"/>（计时归零、面朝异常源）。
        /// FSM <see cref="SceneObjBase.ChangeState"/> 对\"目标==当前\"直接 return，
        /// 因此若当前已是 Alerted，先经 Idle 中转一步保证 Enter 回调重跑。
        /// </summary>
        private void ForceReenterAlerted()
        {
            if (StateName == "Alerted")
            {
                ChangeState("Idle");
            }
            ChangeState("Alerted");
        }

        #endregion
    }
}
