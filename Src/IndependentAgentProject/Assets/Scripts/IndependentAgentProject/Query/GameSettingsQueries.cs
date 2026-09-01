using FrameworkDesign;

namespace IndependentAgentProject
{
    /// <summary>
    /// 画面设置只读数据快照（v0.23.4）。
    /// 由 <see cref="GetGameSettingsQuery"/> 返回，只包含使用方（UISetting）所需字段，不多给。
    /// </summary>
    public readonly struct GameSettingsSnapshot
    {
        /// <summary>当前显示模式下标（0 窗口化 / 1 无边框 / 2 全屏）。</summary>
        public readonly int DisplayModeIndex;

        /// <summary>当前分辨率预置下标。</summary>
        public readonly int ResolutionIndex;

        /// <summary>当前值相对已保存值是否有未保存变更。</summary>
        public readonly bool HasChanged;

        public GameSettingsSnapshot(int displayModeIndex, int resolutionIndex, bool hasChanged)
        {
            DisplayModeIndex = displayModeIndex;
            ResolutionIndex = resolutionIndex;
            HasChanged = hasChanged;
        }
    }

    /// <summary>
    /// 获取画面设置数据（v0.23.4 MVC 化）。
    /// 统一数据获取入口：返回一个 <see cref="GameSettingsSnapshot"/>，
    /// 只含 UISetting 所需的 3 个字段（显示模式下标 / 分辨率下标 / 是否有变更）。
    /// </summary>
    public class GetGameSettingsQuery : AbstractQuery<GameSettingsSnapshot>
    {
        protected override GameSettingsSnapshot OnDo()
        {
            var m = this.GetModel<IGameSettingsModel>();
            var (_, savedMode, savedRes) = Services.GameSettingsStore.Load();
            bool hasChanged = m.DisplayModeIndex.Value != savedMode
                || m.ResolutionIndex.Value != savedRes;
            return new GameSettingsSnapshot(m.DisplayModeIndex.Value, m.ResolutionIndex.Value, hasChanged);
        }
    }
}
