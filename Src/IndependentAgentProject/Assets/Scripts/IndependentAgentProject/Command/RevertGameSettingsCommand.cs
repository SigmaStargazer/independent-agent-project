using FrameworkDesign;
using Services;

namespace IndependentAgentProject
{
    /// <summary>
    /// 还原画面设置（v0.23.4）。
    /// 从 GameSettingsStore 读已保存值写回 Model（MsgboxSaveSetting「退出」时由 UITitle 发送）。
    /// 值变化会触发 IGameSettingsModel 的 BindableProperty 回调 → 自动 ApplyScreen + 刷新 UI，
    /// 从而同时还原实际显示模式/分辨率与设置面板显示的数值。
    /// </summary>
    public class RevertGameSettingsCommand : AbstractCommand
    {
        protected override void OnExecute()
        {
            var model = this.GetModel<IGameSettingsModel>();
            var (_, mode, res) = GameSettingsStore.Load();
            model.DisplayModeIndex.Value = mode;
            model.ResolutionIndex.Value = res;
        }
    }
}
