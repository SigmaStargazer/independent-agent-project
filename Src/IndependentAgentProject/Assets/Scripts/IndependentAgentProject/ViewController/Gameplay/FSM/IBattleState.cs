namespace IndependentAgentProject
{
    /// <summary>
    /// FSM 标记接口：实现此接口的 FSMState 表示「角色处于战斗状态」，
    /// 不应被异常事件（EnemyAnomalyEvent 等）干扰。
    /// SceneObjBase.IsInBattle 会通过 (mCurState is IBattleState) 统一判定，
    /// 业务代码（EnemyBase.OnHearAnomaly 等）不应再用状态名字符串判断。
    /// 本期实现者：EnemyBase.ChaseState、EnemyBase.SearchingState。
    /// </summary>
    public interface IBattleState { }
}
