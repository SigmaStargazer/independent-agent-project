using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrameworkDesign
{
    public interface ITypeEventSystem 
    {
        void Send<T>() where T: new();
        void Send<T>(T e);
        IUnRegister Register<T>(Action<T> onEvent);
        void UnRegister<T>(Action<T> onEvent);
    }

    public interface IUnRegister
    {
        void UnRegister();
    }
    /// <summary>
    /// 用于在Register<T>返回一个对象，方便后面注销
    /// </summary>
    public struct TypeEventSystemUnRegister<T> : IUnRegister
    {
        public ITypeEventSystem TypeEventSystem;
        public Action<T> OnEvent;

        public void UnRegister()
        {
            TypeEventSystem.UnRegister<T>(OnEvent);
            TypeEventSystem = null;
            OnEvent = null;
        }
    }

    /// <summary>
    /// 通过UnRegisterWhenGameObjectDestroyed方法，挂载到gameObject的脚本。
    /// 该脚本 OnDestroy时会注销gameObject
    /// </summary>
    public class UnRegisterOnDestroyTrigger : MonoBehaviour
    {
        HashSet<IUnRegister> mUnRegisters = new HashSet<IUnRegister>();
        public void AddUnRegister(IUnRegister unRegister)
        {
            mUnRegisters.Add(unRegister);
        }

        private void OnDestroy()
        {
            foreach (var unRegister in mUnRegisters)
            {
                unRegister.UnRegister();
            }

            mUnRegisters.Clear();
        }
    }

    public static class UnRegisterExtension
    {
        public static void UnRegisterWhenGameObjectDestroyed(this IUnRegister unRegister, GameObject gameObject)
        {
            var trigger = gameObject.GetComponent<UnRegisterOnDestroyTrigger>();
            //如果没有触发器脚本，那就加上一个
            if (!trigger)
            {
                trigger = gameObject.AddComponent<UnRegisterOnDestroyTrigger>();
            }

            trigger.AddUnRegister(unRegister);
        }
    }

    public class TypeEventSystem : ITypeEventSystem
    {
        public interface IRegistrations
        {

        }

        public class Registrations<T> : IRegistrations
        {
            //防止委托为空
            public Action<T> OnEvent = e => { };
        }

        Dictionary<Type, IRegistrations> mEventRegistration = new Dictionary<Type, IRegistrations>();

        public IUnRegister Register<T>(Action<T> onEvent)
        {
            var type = typeof(T);
            IRegistrations registrations;

            if (mEventRegistration.TryGetValue(type, out registrations))//如果为true，存在type的对象registrations
            {

            }
            else
            {
                registrations = new Registrations<T>();
                mEventRegistration.Add(type, registrations);
            }

            (registrations as Registrations<T>).OnEvent += onEvent;

            return new TypeEventSystemUnRegister<T>()
            {
                OnEvent = onEvent,
                TypeEventSystem = this
            };
        }
        public void UnRegister<T>(Action<T> onEvent)
        {
            var type = typeof(T);
            IRegistrations registrations;

            if (mEventRegistration.TryGetValue(type, out registrations))//如果为true，存在type的对象registrations
            {
                (registrations as Registrations<T>).OnEvent -= onEvent;
            }
        }
        public void Send<T>() where T : new()
        {
            var e = new T();
            Send<T>(e);
        }

        public void Send<T>(T e)
        {
            var type = typeof(T);
            IRegistrations registrations;

            if (mEventRegistration.TryGetValue(type, out registrations))//如果为true，存在type的对象registrations
            {
                (registrations as Registrations<T>).OnEvent(e);
            }
        }
    }
}

