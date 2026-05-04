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
