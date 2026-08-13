namespace IndependentAgentProject
{
    /// <summary>
    /// 交互动画标签。由 <see cref="IInteractable"/> 的 Interact/Select/TextInput 返回值携带，
    /// 告知调用方本次交互应播放哪种动作动画。
    /// <para><see cref="None"/> 表示不播动作动画（状态切换型交互靠 FSM 驱动；或无需动画）。</para>
    /// </summary>
    public enum InteractAnimTag
    {
        None,       // 不播动作动画
        Interact,   // 通用交互
        Select,     // 选择
        TextInput,  // 文本输入
        Backstab,   // 背刺
        Trade,      // 交易
        Steal,      // 盗窃
    }
}
