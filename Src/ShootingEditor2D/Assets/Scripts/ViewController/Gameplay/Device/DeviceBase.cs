using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class DeviceBase : SceneObjBase, IClickable
    {
        public virtual bool IsClickable => false;

        public virtual void OnClick()
        {
            // 默认空实现
        }
        public abstract bool IsInteractable { get; }
        protected virtual void OnEnable()
        {
            if (DeviceManager.Instance != null)
                DeviceManager.Instance.Register(this);
        }

        protected virtual void OnDisable()
        {
            if (DeviceManager.Instance != null)
                DeviceManager.Instance.UnRegister(this);
        }
        public virtual (bool success, string result) Interact(GameObject chara)
        {
            return (false, "该设备无法交互");
        }
        public virtual (bool success, string result) Select(GameObject chara, int selection)
        {
            return (false,"该设备未提供选项");
        }
        public virtual (bool success, string result) TextInput(GameObject chara, string inputText)
        {
            return (false, "该设备未提供输入框");
        }
    }
}
