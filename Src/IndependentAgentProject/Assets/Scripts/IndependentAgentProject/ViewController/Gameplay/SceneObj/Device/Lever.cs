using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Lever : DeviceBase
    {
        public override string Name => "拉杆";
        public override string Desc => "拉动后似乎能控制什么的装置。";
        public override bool IsInteractable => true;
        [Header("触发目标")]
        [SerializeField]
        private List<MonoBehaviour> mTargets = new();

        public override (bool success, string result, InteractAnimTag animTag) Interact(GameObject chara)
        {
            bool success = false;
            foreach (var target in mTargets)
            {
                if (target is not ITriggerable triggerable)
                    continue;
                if (!triggerable.CanTrigger())
                    continue;
                triggerable.Trigger();
                success = true;
            }
            return (success,
                success ? "拉动后似乎有什么装置动了。" : "拉动后似乎没有作用。",
                success ? InteractAnimTag.Interact : InteractAnimTag.None);
        }
    }
}