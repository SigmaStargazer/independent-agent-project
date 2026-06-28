namespace IndependentAgentProject
{
    /// <summary>
    /// FSM 标记接口：实现此接口的 FSMState 表示「该状态下角色/物体免疫一切致死伤害与受伤型重生」。
    /// 例如 CharaBase.DeadState、PlayerBase.HiddenState。
    /// SceneObjBase.IsInvulnerable 通过 (mCurState is IInvulnerableState) 统一判定。
    /// 现有所有伤害源（Laser / Abyss / EnemyBase 攻击）最终都调用 CharaBase.Die()，
    /// Die() 入口会用 IsInvulnerable 直接拦截；受伤型传送走 PlayerBase.ReturnToCheckPointByHurt，
    /// 同样以 IsInvulnerable 拦截。中性的 ReturnToCheckPoint（调试/系统重置）不走该判定。
    /// </summary>
    public interface IInvulnerableState { }
}
