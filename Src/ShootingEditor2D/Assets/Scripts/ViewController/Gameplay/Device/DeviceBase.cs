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
        public abstract string Interact(GameObject chara);
        public virtual string Select(GameObject chara, int selection)
        {
            return "该设备未提供选项";
        }
        public virtual string TextInput(GameObject chara, string inputText)
        {
            return "该设备未提供输入框";
        }
    }
}
