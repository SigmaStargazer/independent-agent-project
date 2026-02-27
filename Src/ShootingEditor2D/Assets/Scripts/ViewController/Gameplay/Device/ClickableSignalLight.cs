using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static ShootingEditor2D.Safebox;

namespace ShootingEditor2D
{
    public class ClickableSignalLight : DeviceBase
    {
        public override string Name => "ÐÅºÅµÆ";
        public override string Desc => "ÓÐºìµÆºÍÂÌµÆÁ½ÖÖ×´Ì¬";

        public override bool IsInteractable => false;

        public override bool IsClickable => true;

        protected override void Awake()
        {
            //Ìí¼Ó×´Ì¬
            RegisterState(new GreenLightState());
            RegisterState(new RedLightState());
        }

        protected override void Start()
        {
            // Ä¬ÈÏ½øÈëClose×´Ì¬
            ChangeState("RedLight");
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

        public override void OnClick()
        {
            SwitchLight();
        }

        public void SwitchLight()
        {
            if (StateName == "RedLight")
                ChangeState("GreenLight");
            else if (StateName == "GreenLight")
                ChangeState("RedLight");
            Debug.Log($"ÐÅºÅµÆ×´Ì¬ÇÐ»»:{StateName}");
        }
    }
}

