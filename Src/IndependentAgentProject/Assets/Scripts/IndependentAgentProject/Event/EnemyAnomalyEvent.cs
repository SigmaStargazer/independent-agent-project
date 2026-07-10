using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 敌人 AI 感知用的「异常源事件」。发送方是任意声源（碎玻璃、脚步、玩家说话等），
    /// 接收方是订阅了本事件的 AI 单位（当前仅 EnemyBase）。
    /// 命名以 Enemy 前缀，明确「AI 感知」语义，避免与「系统异常/错误」混淆。
    ///
    /// <para>Triggerer 是「引发本次异常源」的场景对象（谁踩了碎玻璃）；接收方基于此分流：</para>
    /// <list type="bullet">
    ///   <item>Triggerer == 自己：忽略（避免自触发死循环）。</item>
    ///   <item>Triggerer is EnemyBase 且 != 自己：仅警觉（Alerted 后回上一状态或继续原调查）。</item>
    ///   <item>其他（PlayerBase / 装置自身 / null）：完整调查流程。</item>
    /// </list>
    ///
    /// <para>SourceDevice 是声源装置本身（BrokenGlass 实例）；接收方基于此维护
    /// 「当前调查源不打断」以及「每敌人对每源的独立冷却」两条过滤规则。</para>
    /// </summary>
    public class EnemyAnomalyEvent
    {
        public Vector2 SourcePos;
        public float Radius;
        public SceneObjBase Triggerer;
        public SceneObjBase SourceObj;
    }
}
