using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 柜子：可被玩家交互的躲藏装置。
    /// 极简设计——本身不注册任何自定义 FSM 状态，沿用 SceneObjBase 默认 Idle。
    /// 通过两个 Transform 锚点完成「进入 / 离开」的瞬移，并直接切换玩家 FSM 状态：
    /// - 玩家不在 Hidden 时交互：瞬移到 mEnterAnchor 并 ChangeState("Hidden")。
    /// - 玩家已在 Hidden 时交互：瞬移到 mExitAnchor 并 ChangeState("Idle")。
    /// </summary>
    public class Cabinet : DeviceBase
    {
        public override string Name => "柜子";
        public override string Desc => "可以躲进去的柜子。";
        public override bool IsInteractable => true;

        [Header("玩家锚点")]
        [SerializeField][Tooltip("玩家进入柜子时被瞬移到此位置")]
        private Transform mEnterAnchor;
        [SerializeField][Tooltip("玩家离开柜子时被瞬移到此位置")]
        private Transform mExitAnchor;

        public override (bool success, string result) Interact(GameObject chara)
        {
            PlayerBase player = chara != null ? chara.GetComponent<PlayerBase>() : null;
            if (player == null)
                return (false, "只有玩家才能使用柜子。");
            if (player.IsDead)
                return (false, "已经死了，无法使用柜子。");

            if (player.StateName != "Hidden")
            {
                if (mEnterAnchor == null)
                    return (false, "柜子的进入位置未配置。");
                player.transform.position = mEnterAnchor.position;
                // v0.21.7-fix: 速度归零与位置冻结由 PlayerBase.OnHiddenEnter 统一处理（FreezeAll + 归零），Cabinet 不再双重管控。
                player.ChangeState("Hidden");
                return (true, "你躲进了柜子里。");
            }
            else
            {
                if (mExitAnchor == null)
                    return (false, "柜子的离开位置未配置。");
                player.transform.position = mExitAnchor.position;
                // v0.21.7-fix: Hidden 期间速度本就被 FreezeAll 锁定为 0；OnHiddenExit 只还原 constraints，Cabinet 无需再清速度。
                player.ChangeState("Idle");
                return (true, "你从柜子里出来了。");
            }
        }
    }
}
