using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class FSMStateBase
    {
        public abstract string Name { get; }

        public virtual void OnEnter(DeviceBase device) { }
        public virtual void OnExit(DeviceBase device) { }
        public virtual void OnUpdate(DeviceBase device) { }
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
