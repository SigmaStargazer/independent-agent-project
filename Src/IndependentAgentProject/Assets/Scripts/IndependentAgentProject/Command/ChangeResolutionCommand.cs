using FrameworkDesign;

namespace IndependentAgentProject
{
    /// <summary>
    /// 修改画面分辨率（v0.23.4 MVC 化）。
    /// View（UISetting）点箭头 → SendCommand → 改 Model → BindableProperty 事件驱动 UI 刷新。
    /// </summary>
    public class ChangeResolutionCommand : AbstractCommand
    {
        private readonly int mDelta;   // +1 下一档 / -1 上一档

        public ChangeResolutionCommand(int delta)
        {
            mDelta = delta;
        }

        protected override void OnExecute()
        {
            var model = this.GetModel<IGameSettingsModel>();
            int next = model.ResolutionIndex.Value + mDelta;
            model.ResolutionIndex.Value = next;
        }
    }
}
