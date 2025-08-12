namespace FrameworkDesign
{
    public interface IModel : IBelongToArchitecture, ICanSetArchitecture, 
        ICanGetUtility, ICanSendEvent
    {
        void Init();
    }

    public abstract class AbstractModel : IModel
    {
        private IArchitecture mArchitecture;
        void IModel.Init()
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