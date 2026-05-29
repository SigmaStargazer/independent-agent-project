using System;

namespace FrameworkDesign
{
    public class BindableProperty<T>
    {
        public T mValue = default(T);//比如int为0、bool为false等
        public T Value
        {
            get => mValue;
            set
            {
                if(!value.Equals(mValue))
                {
                    mValue = value;

                    //OnValueChanged?.Invoke(Value);
                    mOnValueChanged?.Invoke(Value);
                }
            }
        }
        //public Action<T> OnValueChanged;
        private Action<T> mOnValueChanged = (v) => { };
        public IUnRegister RegisterOnValueChanged(Action<T> onValueChanged)
        {
            mOnValueChanged = onValueChanged;
            return new BindablePropertyUnRegister<T>()
            {
                bindableProperty = this,
                OnValueChanged = onValueChanged
            };
        }
        public void UnRegisterOnValueChanged(Action<T> onValueChanged)
        {
            mOnValueChanged -= onValueChanged;
        }
    }
    //IUnRegister是TypeEventSystem部分里，返回的用于注销的类
    //记录了bindableProperty、其上注册的OnValueChanged方法，用于把OnValueChanged从bindableProperty注销掉
    public class BindablePropertyUnRegister<T> : IUnRegister
    {
        public BindableProperty<T> bindableProperty { get; set; }
        public Action<T> OnValueChanged { get; set; }
        public void UnRegister()
        {
            bindableProperty.UnRegisterOnValueChanged(OnValueChanged);
            bindableProperty = null;
            OnValueChanged = null;
        }
    }
}


