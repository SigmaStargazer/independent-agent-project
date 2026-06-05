using FrameworkDesign;
using IndependentAgentProject;
using Services;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class PlayerBase : CharaBase
    {
        public override bool IsInteractable => false;

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

    }
}