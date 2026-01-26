namespace ShootingEditor2D
{
    public abstract class FSMStateBase
    {
        public abstract string Name { get; }

        public virtual void OnEnter(SceneObjBase device) { }
        public virtual void OnExit(SceneObjBase device) { }
        public virtual void OnUpdate(SceneObjBase device) { }
    }


    public sealed class IdleState : FSMStateBase
    {
        public override string Name => "Idle";
    }

    public sealed class MoveState : FSMStateBase
    {
        public override string Name => "Move";
    }
}
