using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static ShootingEditor2D.Safebox;

namespace ShootingEditor2D
{
    public class SignalLight : DeviceBase
    {
        public override string Name => "ÐÅºÅµÆ";
        public override string Desc => "ÓÐºìµÆºÍÂÌµÆÁ½ÖÖ×´Ì¬";

        public override bool IsInteractable => false;

        protected override void Awake()
        {
            //Ìí¼Ó×´Ì¬
            RegisterState(new GreenLightState());
            RegisterState(new RedLightState());
            //ÉèÖÃ³õÊ¼×´Ì¬
            curState = states["RedLight"];
            curState.OnEnter(this);
        }

        public override string Interact(GameObject chara)
        {
            return "";
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

