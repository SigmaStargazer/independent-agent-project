namespace IndependentAgentProject
{
    /// <summary>
    /// FSM 标记接口：实现此接口的 FSMState 不会被 EnemyBase 等敌对单位检测/追击。
    /// 命名加 "State" 后缀，明确这是 FSMStateBase 的接口（而非角色/对象的接口）。
    /// 例如 PlayerBase.HiddenState、CharaBase.DeadState、EnemyBase.StunnedState。
    /// SceneObjBase.IsUndetectable 会通过 (mCurState is IUndetectableState) 统一判定，
    /// 业务代码（敌人视野检测等）不应再用状态名字符串判断。
    /// </summary>
    public interface IUndetectableState { }
}
