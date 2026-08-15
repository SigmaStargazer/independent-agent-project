using FrameworkDesign;
using IndependentAgentProject;
using Services;
using ShootingEditor2D;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class PlayerBase : CharaBase
    {
        public override bool IsInteractable => false;

        /// <summary>最近接触到的 CheckPoint。被 CheckPoint.OnTriggerEnter2D 调用 UpdateCheckPoint 时刷新。</summary>
        public CheckPoint LastCheckPoint { get; private set; }

        // v0.21.7-fix: Hidden 状态进入时保存的 Rigidbody2D 约束，退出时无条件还原。
        private RigidbodyConstraints2D mHiddenSavedConstraints;
        // v0.21.7-fix: Hidden 进入时被禁用的 Renderer 列表，退出时按列表还原（避免把进入前就 disabled 的也开起来）。
        private readonly List<Renderer> mHiddenDisabledRenderers = new List<Renderer>();

        // v0.22.18: 缓存 PlayerAnimator 组件，供交互动画播放使用
        protected PlayerAnimator mPlayerAnimator;

        protected override void Awake()
        {
            base.Awake();
            mPlayerAnimator = GetComponent<PlayerAnimator>();
            RegisterState(new HiddenState());
            this.RegisterEvent<GameOverEvent>(e =>
            {
                if (this.GetStateName() != "Dead")
                {
                    ChangeState("Idle");
                }
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        #region Hidden Hook（v0.21.7 躲藏功能；柜子 Cabinet 负责进入 / 退出）
        /// <summary>
        /// 进入 Hidden 状态：
        /// 1) 保存当前 Rigidbody2D.constraints 并切到 FreezeAll，彻底锁定位置/旋转（重力/外力/平台均无法改变坐标）；
        /// 2) 速度与角速度归零，避免冻结瞬间残留动量；
        /// 3) 关闭玩家及所有子节点上当前 enabled 的 Renderer（SpriteRenderer/MeshRenderer/ParticleSystemRenderer 等），
        ///    并记录到列表用于 OnHiddenExit 时还原——不动 GameObject.activeSelf 以保留 FSM/Collider/Trigger/脚本运行。
        /// </summary>
        public virtual void OnHiddenEnter()
        {
            if (mRigidbody2D != null)
            {
                mHiddenSavedConstraints = mRigidbody2D.constraints;
                mRigidbody2D.constraints = RigidbodyConstraints2D.FreezeAll;
                mRigidbody2D.velocity = Vector2.zero;
                mRigidbody2D.angularVelocity = 0f;
            }

            mHiddenDisabledRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (r != null && r.enabled)
                {
                    mHiddenDisabledRenderers.Add(r);
                    r.enabled = false;
                }
            }
        }
        public virtual void OnHiddenUpdate() { }
        public virtual void OnHiddenFixedUpdate() { }
        /// <summary>
        /// 退出 Hidden 状态：
        /// 1) 无条件还原 constraints 到进入前保存的值（D6：如未来 Dead 要加约束，由 DeadState 自行设置）；
        /// 2) 按 OnHiddenEnter 记录的列表把被关闭的 Renderer.enabled 还原为 true。
        /// </summary>
        public virtual void OnHiddenExit()
        {
            if (mRigidbody2D != null)
            {
                mRigidbody2D.constraints = mHiddenSavedConstraints;
            }

            foreach (var r in mHiddenDisabledRenderers)
            {
                if (r != null) r.enabled = true;
            }
            mHiddenDisabledRenderers.Clear();
        }
        #endregion

        #region FSM Hook
        public override void OnIdleEnter()
        {
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }

        public override void OnMoveEnter()
        {
            // v0.22.21: 转向收敛到 CharaBase.OnMoveFixedUpdate（读 velocity.x），
            // OnMoveEnter 不再翻转——首帧朝向由同物理帧内随后执行的 OnMoveFixedUpdate 按速度修正。
        }

        public override void OnMoveFixedUpdate()
        {
            float dir = moveRight ? 1f : -1f;
            // v0.22.21: 只写速度；转向由 CharaBase.OnMoveFixedUpdate 按本帧刚写入的 velocity.x 处理。
            // 所有基于 moveRight 的移动（HumanPlayer 输入 / AIPlayer Move 工具 / ActionSequence MoveAction）
            // 都汇聚到 Move 状态的 OnMoveFixedUpdate，每帧执行、不受 ChangeState 同状态去重影响。
            mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
            base.OnMoveFixedUpdate();
        }

        public override void OnMoveExit()
        {
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }

        public override void OnDeadEnter()
        {
            this.SendCommand<KillPlayerCommand>();
        }
        #endregion

        #region CheckPoint（v0.21.0 训练场）

        /// <summary>由 CheckPoint.OnTriggerEnter2D 调用：把当前 CheckPoint 设为最新重生点。</summary>
        public virtual void UpdateCheckPoint(CheckPoint cp)
        {
            Debug.Log($"[{Name}] 到达检查点");
            LastCheckPoint = cp;
        }

        /// <summary>
        /// 中性版本：返回最近 CheckPoint 的重生锚点。语义为「我决定回到检查点」（调试命令 / 系统重置），
        /// 不检查 IsInvulnerable——无敌状态下也会被传送。受伤型传送请调 ReturnToCheckPointByHurt。
        /// v0.21.7-fix_3：参数从 SceneObjBase 退化为 string sourceName，仅作子类（AIPlayer）反馈消息显示名，
        /// 基类不使用。这样可让"伤害源"无须是 SceneObjBase（如 LaserTraining 等子物体 MonoBehaviour 也可调用）。
        /// </summary>
        public virtual void ReturnToCheckPoint(string sourceName = null)
        {
            if (LastCheckPoint == null)
            {
                Debug.Log($"[{Name}] 没有最后的检查点");
                return;
            }
            Debug.Log($"[{Name}] 返回最后的检查点");
            transform.position = LastCheckPoint.GetRespawnPosition();
            if (mRigidbody2D != null)
            {
                mRigidbody2D.velocity = Vector2.zero;
                mRigidbody2D.angularVelocity = 0f;
            }
            ChangeState("Idle");
        }

        /// <summary>
        /// 受伤型版本：由 Trap / LaserTraining 等伤害性机关触发。语义为「玩家被伤害性机关击中，传送回检查点」。
        /// 当 IsInvulnerable（如 Hidden / Dead）时直接拒绝，符合 v0.21.7-fix 的免疫语义。
        /// AIPlayer 会 override 以追加 StopMovement(true) 与反馈消息。
        /// v0.21.7-fix_3：参数从 SceneObjBase 退化为 string sourceName（仅作显示用）。
        /// </summary>
        public virtual void ReturnToCheckPointByHurt(string sourceName = null)
        {
            if (IsInvulnerable) return;
            ReturnToCheckPoint(sourceName);
        }

        #endregion

        #region Hidden FSM State
        /// <summary>
        /// 躲藏状态：实现 IUndetectableState（不被敌人检测/追击）、IImmovableState（屏蔽主动位移）、
        /// IInvulnerableState（CharaBase.Die() 与 PlayerBase.ReturnToCheckPointByHurt 入口免疫）。
        /// 进入/退出由柜子 Cabinet.Interact 直接调用 player.ChangeState 切换。
        /// </summary>
        public class HiddenState : FSMStateBase, IUndetectableState, IImmovableState, IInvulnerableState
        {
            public override string Name => "Hidden";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is PlayerBase player) player.OnHiddenEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is PlayerBase player) player.OnHiddenUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is PlayerBase player) player.OnHiddenFixedUpdate();
            }
            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is PlayerBase player) player.OnHiddenExit();
            }
        }
        #endregion
    }
}
