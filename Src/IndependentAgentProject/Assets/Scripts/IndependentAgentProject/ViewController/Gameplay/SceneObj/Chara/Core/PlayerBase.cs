using FrameworkDesign;
using IndependentAgentProject;
using Services;
using ShootingEditor2D;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class PlayerBase : CharaBase
    {
        public override bool IsInteractable => false;

        /// <summary>最近接触到的 CheckPoint。被 CheckPoint.OnTriggerEnter2D 调用 UpdateCheckPoint 时刷新。</summary>
        public CheckPoint LastCheckPoint { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            this.RegisterEvent<GameOverEvent>(e =>
            {
                if (this.GetStateName() != "Dead")
                {
                    ChangeState("Idle");
                }
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        #region FSM Hook
        public override void OnIdleEnter()
        {
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }

        public override void OnMoveEnter()
        {
            float dir = moveRight ? 1f : -1f;
            TurnBack(dir);
        }

        public override void OnMoveFixedUpdate()
        {
            float dir = moveRight ? 1f : -1f;
            mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
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
        /// 返回最近 CheckPoint 的重生锚点：
        /// 1. 取 LastCheckPoint.GetRespawnPosition()（默认 respawnAnchor，未挂则用 CheckPoint 自身位置）
        /// 2. 速度归零（线速度 + 角速度），避免重生后继续滑行
        /// 3. 切到 Idle 状态
        /// 4. LastCheckPoint == null 时只打印警告并返回——避免被瞬移到错误位置
        ///
        /// AIPlayer 会覆写：在 base.ReturnToCheckPoint() 之前先 StopMovement(true) 中断 ActionSequence，
        /// 完成后再 SendFeedbackToAgent 通知 LLM。
        /// </summary>
        public virtual void ReturnToCheckPoint(SceneObjBase sceneObjBase)
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

        #endregion
    }
}
