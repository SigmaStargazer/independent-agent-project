using FrameworkDesign;
using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class CharaBase : SceneObjBase, IInteractable, IController
    {
        public IArchitecture GetArchitecture()
        {
            return ShootingEditor2D.Instance;
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

        public (bool success, string result) Interact(GameObject chara)
        {
            return (false, "该对象无法交互");
        }

        public (bool success, string result) Select(GameObject chara, int selection)
        {
            return (false, "该对象无法交互");
        }

        public (bool success, string result) TextInput(GameObject chara, string inputText)
        {
            return (false, "该对象无法交互");
        }
    }

    //public abstract class CharaBase : SceneObjBase, IController
    //{
    //    public IArchitecture GetArchitecture()
    //    {
    //        return ShootingEditor2D.Instance;
    //    }
    //}

}
