using System;
using System.Collections.Generic;

namespace FrameworkDesign
{

    public interface IArchitecture
    {
        /// <summary>
        /// 注册 System
        /// </summary>
        void RegisterSystem<T>(T instance) where T : ISystem;
        /// <summary>
        /// 注册 Model
        /// </summary>
        void RegisterModel<T>(T instance) where T : IModel;

        /// <summary>
        /// 注册 Utility
        /// </summary>
        void RegisterUtility<T>(T instance) where T : IUtility;

        /// <summary>
        /// 获取System
        /// </summary>
        T GetSystem<T>() where T : class, ISystem;

        /// <summary>
        /// 获取Model
        /// </summary>
        T GetModel<T>() where T : class, IModel;

        /// <summary>
        /// 获取工具
        /// </summary>
        T GetUtility<T>() where T : class, IUtility;

        void SendCommand<T>() where T : ICommand, new();
        void SendCommand<T>(T command) where T : ICommand;

        IUnRegister RegisterEvent<T>(Action<T> onEvent);
        void UnRegisterEvent<T>(Action<T> onEvent);
        void SendEvent<T>() where T : new();
        void SendEvent<T>(T Event);

        TResult SendQuery<TResult>(IQuery<TResult> query);
    }

    /// <summary>
    /// 架构
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class Architecture<T> : IArchitecture where T : Architecture<T>, new()
    {
        // 留给子类注册模块
        protected abstract void Init();

        private IOCContainer mContainer = new IOCContainer();
        /// <summary>
        /// 是否已经初始化完成
        /// </summary>
        private bool mInited = false;

        /// <summary>
        /// 用于初始化的 Systems 的缓存
        /// </summary>
        private List<ISystem> mSystems = new List<ISystem>();

        /// <summary>
        /// 用于初始化的 Models 的缓存
        /// </summary>
        private List<IModel> mModels = new List<IModel>();
        /// <summary>
        /// 增加注册
        /// </summary>
        public static Action<T> OnRegisterPatch = architecture => { };

        private static T mArchitecture = null;

        public static IArchitecture Instance
        {
            get
            {
                if (mArchitecture == null)
                {
                    MakeSureArchitecture();
                }
                return mArchitecture;
            }
        }
        #region 类似单例模式 但是仅在内部课访问

        // 确保 Container 是有实例的
        static void MakeSureArchitecture()
        {
            if (mArchitecture == null)
            {
                mArchitecture = new T();
                mArchitecture.Init();

                // 调用
                OnRegisterPatch?.Invoke(mArchitecture);

                // 初始化 Model
                foreach (var architectureModel in mArchitecture.mModels)
                {
                    architectureModel.Init();
                }

                // 清空 Model
                mArchitecture.mModels.Clear();

                // 初始化 System
                foreach (var architectureSystem in mArchitecture.mSystems)
                {
                    architectureSystem.Init();
                }

                // 清空 System
                mArchitecture.mSystems.Clear();

                mArchitecture.mInited = true;
            }
        }

        #endregion

        public void RegisterSystem<T>(T instance) where T : ISystem
        {
            // 需要给 Model 赋值一下
            instance.SetArchitecture(this);
            mContainer.Register<T>(instance);

            // 如果初始化过了
            if (mInited)
            {
                instance.Init();
            }
            else
            {
                // 添加到 Model 缓存中，用于初始化
                mSystems.Add(instance);
            }
        }
        // 提供一个注册 Model 的 API
        public void RegisterModel<T>(T instance) where T : IModel
        {
            // 需要给 Model 赋值一下
            instance.SetArchitecture(this);
            mContainer.Register<T>(instance);

            // 如果初始化过了
            if (mInited)
            {
                instance.Init();
            }
            else
            {
                // 添加到 Model 缓存中，用于初始化
                mModels.Add(instance);
            }
        }
        public void RegisterUtility<T>(T instance) where T : IUtility
        {
            mContainer.Register<T>(instance);
        }
        public T GetSystem<T>() where T : class, ISystem
        {
            return mContainer.Get<T>();
        }
        public T GetModel<T>() where  T : class,IModel
        {
            return mContainer.Get<T>();
        }
        public T GetUtility<T>() where T : class,IUtility
        {
            return mContainer.Get<T>();
        }
        public void SendCommand<T>() where T : ICommand, new()
        {
            var command = new T();
            command.SetArchitecture(this);
            command.Execute();
            command.SetArchitecture(null);
        }

        public void SendCommand<T>(T command) where T : ICommand
        {
            command.SetArchitecture(this);
            command.Execute();
            command.SetArchitecture(null);
        }

        private ITypeEventSystem mTypeEventSystem = new TypeEventSystem();

        public IUnRegister RegisterEvent<T>(Action<T> onEvent)
        {
            return mTypeEventSystem.Register<T>(onEvent);
        }

        public void UnRegisterEvent<T>(Action<T> onEvent)
        {
            mTypeEventSystem.UnRegister<T>(onEvent);
        }
        public void SendEvent<T>() where T : new()
        {
            mTypeEventSystem.Send<T>();
        }

        public void SendEvent<T>(T e)
        {
            mTypeEventSystem.Send<T>(e);
        }

        public TResult SendQuery<TResult>(IQuery<TResult> query)
        {
            query.SetArchitecture(this);
            return query.Do();
        }
    }
}