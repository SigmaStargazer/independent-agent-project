using FrameworkDesign;
using IndependentAgentProject;
using Services;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class PlayerBase : CharaBase
    {
        protected Rigidbody2D mRigidbody2D;
        public float moveSpeed = 5f;
        protected bool moveRight;
        public override bool IsInteractable => false;

        protected override void Awake()
        {
            base.Awake();
            mRigidbody2D = GetComponent<Rigidbody2D>();

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
            base.OnIdleEnter();
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }

        public override void OnMoveEnter()
        {
            base.OnMoveEnter();
            float dir = moveRight ? 1f : -1f;
            TurnBack(dir);
        }

        public override void OnMoveFixedUpdate()
        {
            base.OnMoveFixedUpdate();
            float dir = moveRight ? 1f : -1f;
            mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
        }

        public override void OnMoveExit()
        {
            base.OnMoveExit();
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }

        public override void OnDeadEnter()
        {
            base.OnDeadEnter();
            this.SendCommand<KillPlayerCommand>();
        }
        #endregion

    }
}