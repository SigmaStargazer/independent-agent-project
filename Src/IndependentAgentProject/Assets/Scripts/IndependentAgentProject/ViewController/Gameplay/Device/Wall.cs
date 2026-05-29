using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Wall : DeviceBase
    {
        public override string Name => "墙";
        public override string Desc => "一堵坚固的墙，无法通过";

        public override bool IsInteractable => false;
    }
}
