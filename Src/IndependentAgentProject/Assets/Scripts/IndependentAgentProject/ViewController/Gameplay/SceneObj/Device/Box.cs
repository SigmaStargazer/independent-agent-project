using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Box : DeviceBase
    {
        public override string Name => "箱子";
        public override string Desc => "一个可以被推动的箱子";
        public override bool IsInteractable => false;
    }
}

