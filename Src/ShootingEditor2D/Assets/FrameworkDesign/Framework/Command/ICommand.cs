namespace FrameworkDesign
{
    public interface ICommand : IBelongToArchitecture, ICanSetArchitecture, 
        ICanGetSystem, ICanGetModel, ICanGetUtility,ICanSendEvent,ICanSendCommand, ICanSendQuery
    {
        void Execute();
    }

    public abstract class AbstractCommand : ICommand
    {
        private IArchitecture mArchitecture;
        void ICommand.Execute()
        {
            OnExecute();
        }

        IArchitecture IBelongToArchitecture.GetArchitecture()
        {
            return mArchitecture;
        }

        void ICanSetArchitecture.SetArchitecture(IArchitecture architecture)
        {
            mArchitecture = architecture;
        }

        protected abstract void OnExecute();
    }

}

