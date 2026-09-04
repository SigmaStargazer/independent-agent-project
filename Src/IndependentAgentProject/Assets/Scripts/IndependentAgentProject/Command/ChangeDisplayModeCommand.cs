using FrameworkDesign;

namespace IndependentAgentProject
{
    /// <summary>
    /// 修改画面显示模式（v0.23.4 MVC 化）。
    /// View（UISetting）点箭头 → SendCommand → 改 Model → BindableProperty 事件驱动 UI 刷新。
    /// </summary>
    public class ChangeDisplayModeCommand : AbstractCommand
    {
        private readonly int mDelta;   // +1 下一档 / -1 上一档（由 View 计算合法区间后传入）

        public ChangeDisplayModeCommand(int delta)
        {
            mDelta = delta;
        }

        protected override void OnExecute()
        {
            var model = this.GetModel<IGameSettingsModel>();
            int next = model.DisplayModeIndex.Value + mDelta;
            model.DisplayModeIndex.Value = next;   // 越界由 View 在按钮 interactable 上控制，此处仅累加
        }
    }
}
