using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 训练场用激光：触碰后把玩家传送回最近的检查点（而不是杀死）。
    /// 与杀玩家版 Laser 的区别：调 PlayerBase.ReturnToCheckPointByHurt 而非 Die，可在训练循环中反复使用。
    ///
    /// 设计说明：
    /// 1. 故意不继承 SceneObjBase——本物体应作为父对象（如 LaserGrid）的子物体存在，
    ///    不希望被 SceneObjManager 自动登记进 AI 的可观察 / 可交互列表。AI 看到的应是父对象（激光网），
    ///    而不是每一根 / 每一片子物体激光。
    /// 2. v0.21.7-fix_3 后，sourceName 固定写死 "激光"——多片激光的区分能力由 AIPlayer 反馈中的
    ///    "最后位置（相对方向+距离）"承担。
    /// 3. 启停由父对象（LaserGrid.mLaser）的 GameObject SetActive 控制，本组件不引入开关字段或 FSM。
    /// </summary>
    public class LaserTraining : MonoBehaviour
    {
        private void OnTriggerEnter2D(Collider2D collision)
        {
            PlayerBase player = collision.GetComponent<PlayerBase>();
            if (player != null)
            {
                player.ReturnToCheckPointByHurt("激光");
            }
        }
    }
}
