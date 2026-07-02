using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class SignalLight : DeviceBase
    {
        public override string Name => "信号灯";
        public override string Desc => "有红灯和绿灯两种状态";

        public override bool IsInteractable => false;

        protected override void Awake()
        {
            //添加状态
            RegisterState(new GreenLightState());
            RegisterState(new RedLightState());
        }

        protected override void Start()
        {
            // 默认进入Close状态
            ChangeState("RedLight");
        }

        public class GreenLightState : FSMStateBase
        {
            public override string Name => "GreenLight";
        }

        public class RedLightState : FSMStateBase
        {
            public override string Name => "RedLight";
        }
    }
}

