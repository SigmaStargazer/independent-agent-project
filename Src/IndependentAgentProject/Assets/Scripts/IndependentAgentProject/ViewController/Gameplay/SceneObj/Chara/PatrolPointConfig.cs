using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 巡逻点抵达时对敌人朝向的偏好。
    /// - KeepCurrent：不改朝向，保持敌人自身移动方向。
    /// - Left / Right：强制朝左 / 朝右。
    /// - AutoByNextMove：根据下一个巡逻点的方向翻朝向（配合环形巡逻链，可保证\"抵达即面向下一目标\"）。
    /// </summary>
    public enum PatrolFacing
    {
        KeepCurrent,
        Left,
        Right,
        AutoByNextMove
    }

    /// <summary>
    /// 挂在巡逻点 Transform 上，配置敌人\"从巡逻抵达此点进入 Idle\"瞬间的朝向。
    /// 仅在从 Move → Idle 的抵达时刻被 EnemyBase 读一次，不影响 Chase/Inspect 等其他路径回归的 Idle。
    /// </summary>
    public class PatrolPointConfig : MonoBehaviour
    {
        public PatrolFacing Facing = PatrolFacing.KeepCurrent;
    }
}
