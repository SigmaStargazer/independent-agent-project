using FrameworkDesign;
using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class CharaBase : SceneObjBase, IInteractable
    {
        protected Rigidbody2D mRigidbody2D;
        public float moveSpeed = 5f;
        protected bool moveRight;
        // 面朝方向
        public bool IsRight => transform.localScale.x > 0;
        public bool IsDead => StateName == "Dead";
        public SceneObjBase TargetFollowing { get; protected set; }
        public float FollowMinDistance { get; protected set; }
        public float FollowMaxDistance { get; protected set; }
        public virtual bool IsInteractable => true;
        protected void TurnBack(float horizontalDirection)
        {
            if (horizontalDirection < 0 && transform.localScale.x > 0
                || horizontalDirection > 0 && transform.localScale.x < 0)
            {
                var localScale = transform.localScale;
                localScale.x = -localScale.x;
                transform.localScale = localScale;
            }
        }
        // v0.22.21: Move 状态专属 FixedUpdate hook——转向收敛点。
        // 读当前速度方向（velocity.x）与 Scale.X 符号比对，不一致才翻转（TurnBack 幂等）。
        // Move 状态下 velocity.x 每帧由意图移动代码覆盖（PlayerBase / EnemyBase 的 OnMoveFixedUpdate 先写速度、再调 base），
        // 因此读到的即意图速度方向，天然规避「受击击退」等外力速度（发生在非 Move 状态，不走本 hook）。
        public override void OnMoveFixedUpdate()
        {
            if (mRigidbody2D != null && Mathf.Abs(mRigidbody2D.velocity.x) > 0.01f)
                TurnBack(mRigidbody2D.velocity.x);
        }
        // Dead hooks
        public virtual void OnDeadEnter() { }
        public virtual void OnDeadUpdate() { }
        public virtual void OnDeadFixedUpdate() { }
        public virtual void OnDeadExit() { }

        // Follow hooks
        public virtual void OnFollowEnter() { }
        public virtual void OnFollowUpdate() { }
        public virtual void OnFollowFixedUpdate()
        {
            if (TargetFollowing == null)
            {
                ChangeState("Idle");
                return;
            }

            float delta = TargetFollowing.transform.position.x - transform.position.x;
            float distance = Mathf.Abs(delta);
            // 转向在 Chara 内：Follow 面向跟随目标（后退时速度方向与面朝相反，不能用 velocity.x 推导）
            TurnBack(delta);
            // 超出跟随范围，进行跟随
            if (distance > FollowMaxDistance)
            {
                float dir = Mathf.Sign(delta);
                mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
            }
            // 处于保持范围内，保持距离
            else if (distance < FollowMinDistance)
            {
                float dir = -Mathf.Sign(delta);
                mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
            }
            // 处于合适的范围内，停止移动
            else
            {
                mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
            }
        }
        public virtual void OnFollowExit()
        {
            // v0.21.7_fix_1 F4: Follow 状态退出时清空跟随目标，
            // 覆盖 StopMovement / StartActionSequence / ReturnToCheckPointByHurt / Die
            // 等强切路径，避免「状态不是 Follow 但 TargetFollowing 仍残留」的语义错乱。
            // OnFollowFixedUpdate 内已有的 TargetFollowing = null 作为防御性双保险保留。
            TargetFollowing = null;
        }
        protected override void Awake()
        {
            base.Awake();
            mRigidbody2D = GetComponent<Rigidbody2D>();
            RegisterState(new DeadState());
            RegisterState(new FollowState());
        }

        public class DeadState : FSMStateBase, IUndetectableState, IImmovableState, IInvulnerableState
        {
            public override string Name => "Dead";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnDeadEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnDeadUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnDeadFixedUpdate();
            }

            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnDeadExit();
            }
        }
        public class FollowState : FSMStateBase
        {
            public override string Name => "Follow";

            public override void OnEnter(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnFollowEnter();
            }
            public override void OnUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnFollowUpdate();
            }
            public override void OnFixedUpdate(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnFollowFixedUpdate();
            }

            public override void OnExit(SceneObjBase sceneObj)
            {
                if (sceneObj is CharaBase chara)
                    chara.OnFollowExit();
            }
        }
        public override void ChangeState(string stateName)
        {
            if (StateName == "Dead" && stateName != "Dead")
                return;
            base.ChangeState(stateName);
        }
        public void Die()
        {
            // v0.21.7-fix: IInvulnerableState 下任何来源的致死调用都被入口拦截。
            // 现有三处伤害源（Laser / Abyss / EnemyBase 攻击）最终都走这里，
            // 无需逐个改伤害源；同时也防御已 Dead 状态被重复 Die 触发 OnDeadEnter。
            if (IsInvulnerable) return;
            ChangeState("Dead");
        }

        public virtual (bool success, string result, InteractAnimTag animTag) Interact(GameObject chara)
        {
            return (false, "该对象无法交互", InteractAnimTag.None);
        }
        public virtual (bool success, string result, InteractAnimTag animTag) Select(GameObject chara, int selection)
        {
            return (false, "该对象未提供选项", InteractAnimTag.None);
        }
        public virtual (bool success, string result, InteractAnimTag animTag) TextInput(GameObject chara, string inputText)
        {
            return (false, "该对象未提供输入框", InteractAnimTag.None);
        }
    }
}
