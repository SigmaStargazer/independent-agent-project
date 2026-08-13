using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public abstract class DeviceBase : SceneObjBase, IInteractable,IClickable
    {
        public virtual bool IsClickable => false;

        public virtual void OnClick()
        {
            // 默认空实现
        }
        public abstract bool IsInteractable { get; }
        public virtual (bool success, string result, InteractAnimTag animTag) Interact(GameObject chara)
        {
            return (false, "该对象无法交互", InteractAnimTag.None);
        }
        public virtual (bool success, string result, InteractAnimTag animTag) Select(GameObject chara, int selection)
        {
            return (false,"该对象未提供选项", InteractAnimTag.None);
        }
        public virtual (bool success, string result, InteractAnimTag animTag) TextInput(GameObject chara, string inputText)
        {
            return (false, "该对象未提供输入框", InteractAnimTag.None);
        }
    }
}
