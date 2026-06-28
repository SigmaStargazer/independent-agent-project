namespace IndependentAgentProject
{
    /// <summary>
    /// FSM 标记接口：实现此接口的 FSMState 表示「该状态下角色不可主动移动」。
    /// 例如 CharaBase.DeadState、PlayerBase.HiddenState、EnemyBase.StunnedState。
    /// SceneObjBase.IsImmovable 会通过 (mCurState is IImmovableState) 统一判定，
    /// HumanPlayer 输入读取与 AIPlayer 的 Move/Follow 工具入口、ActionSequence 中
    /// 的 MoveAction/FollowAction 在执行前应检查 IsImmovable 并直接拒绝/返回失败，
    /// 不再用状态名字符串判断。
    /// </summary>
    public interface IImmovableState { }
}
