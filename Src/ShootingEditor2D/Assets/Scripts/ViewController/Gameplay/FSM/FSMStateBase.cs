namespace ShootingEditor2D
{
    public abstract class FSMStateBase
    {
        public abstract string Name { get; }

        public virtual void OnEnter(SceneObjBase sceneObj) { }
        public virtual void OnUpdate(SceneObjBase sceneObj) { }
        public virtual void OnFixedUpdate(SceneObjBase sceneObj) { }
        public virtual void OnExit(SceneObjBase sceneObj) { }
        
    }


    public sealed class IdleState : FSMStateBase
    {
        public override string Name => "Idle";

        public override void OnEnter(SceneObjBase sceneObj) => sceneObj.OnIdleEnter();
        public override void OnUpdate(SceneObjBase sceneObj) => sceneObj.OnIdleUpdate();
        public override void OnFixedUpdate(SceneObjBase sceneObj) => sceneObj.OnIdleFixedUpdate();
        public override void OnExit(SceneObjBase sceneObj) => sceneObj.OnIdleExit();
    }

    public sealed class MoveState : FSMStateBase
    {
        public override string Name => "Move";

        public override void OnEnter(SceneObjBase sceneObj) => sceneObj.OnMoveEnter();
        public override void OnUpdate(SceneObjBase sceneObj) => sceneObj.OnMoveUpdate();
        public override void OnFixedUpdate(SceneObjBase sceneObj) => sceneObj.OnMoveFixedUpdate();
        public override void OnExit(SceneObjBase sceneObj) => sceneObj.OnMoveExit();
    }
}
