namespace IndependentAgentProject
{
    /// <summary>
    /// 是否会被触发（例如开关等）
    /// </summary>
    public interface ITriggerable
    {
        bool CanTrigger();

        void Trigger();
    }
}
