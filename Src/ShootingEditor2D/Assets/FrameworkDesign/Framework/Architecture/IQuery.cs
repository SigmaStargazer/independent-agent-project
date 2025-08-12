namespace FrameworkDesign
{
    public interface IQuery<T> : IBelongToArchitecture, ICanSetArchitecture, 
        ICanGetModel, ICanGetSystem, ICanGetUtility, ICanSendQuery
    {
        T Do();
    }

    public abstract class AbstractQuery<T> : IQuery<T>
    {
        public T Do()
        {
            return OnDo();
        }

        protected abstract T OnDo();

        private IArchitecture mArchitecture;

        IArchitecture IBelongToArchitecture.GetArchitecture()
        {
            return mArchitecture;
        }

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture)
        {
            mArchitecture = architecture;
        }
    }
}