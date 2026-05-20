using FrameworkDesign;
using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class CharaBase : SceneObjBase, IInteractable, IController
    {
        // 面朝方向
        public bool isRight => transform.localScale.x > 0;
        public IArchitecture GetArchitecture()
        {
            return ShootingEditor2D.Instance;
        }
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
        public bool IsInteractable => true;

        // Dead hooks
        public virtual void OnDeadEnter() { }
        public virtual void OnDeadUpdate() { }
        public virtual void OnDeadFixedUpdate() { }
        public virtual void OnDeadExit() { }
        protected override void Awake()
        {
            base.Awake();
            RegisterState(new DeadState());
        }
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

        public class DeadState : FSMStateBase
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

        public virtual (bool success, string result) Interact(GameObject chara)
        {
            return (false, "该对象无法交互");
        }
        public virtual (bool success, string result) Select(GameObject chara, int selection)
        {
            return (false, "该对象未提供选项");
        }
        public virtual (bool success, string result) TextInput(GameObject chara, string inputText)
        {
            return (false, "该对象未提供输入框");
        }
    }
}
