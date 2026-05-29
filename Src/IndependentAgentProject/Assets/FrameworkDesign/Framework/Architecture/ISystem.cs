namespace FrameworkDesign
{
    public interface ISystem: IBelongToArchitecture, ICanSetArchitecture, 
        ICanGetModel, ICanGetUtility,ICanSendEvent,ICanRegisterEvent,ICanGetSystem
    {
        /// <summary>
        /// System有状态，所以需要init
        /// </summary>
        void Init();
    }

    public abstract class AbstractSystem : ISystem
    {
        private IArchitecture mArchitecture;
        void ISystem.Init()
        {
            OnInit();
        }
        //public IArchitecture GetArchitecture()
        IArchitecture IBelongToArchitecture.GetArchitecture()
        {
            return mArchitecture;
        }
        //public void SetArchitecture(IArchitecture architecture)
        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture)
        {
            mArchitecture = architecture;
        }

        protected abstract void OnInit();
    }
}
