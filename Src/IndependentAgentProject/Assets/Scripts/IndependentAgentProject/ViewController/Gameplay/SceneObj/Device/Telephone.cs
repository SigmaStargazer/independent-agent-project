using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Telephone : DeviceBase
    {
        public override string Name => "电话";
        public override string Desc => "一台老式有线电话。";

        public override bool IsInteractable => false;

        public override (bool success, string result) Interact(GameObject chara)
        {
            return (false, "该设备无法交互");
        }
    }

}
