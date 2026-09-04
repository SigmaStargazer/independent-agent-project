using FrameworkDesign;
using Services;

namespace IndependentAgentProject
{
    /// <summary>
    /// 保存画面设置（v0.23.4 MVC 化）。
    /// 把 Model 当前值写入 GameSettingsStore（MsgboxSaveSetting「保存并退出」时由 UITitle 发送）。
    /// </summary>
    public class SaveGameSettingsCommand : AbstractCommand
    {
        protected override void OnExecute()
        {
            var model = this.GetModel<IGameSettingsModel>();
            GameSettingsStore.Save(model.DisplayModeIndex.Value, model.ResolutionIndex.Value, model.Language.Value);
        }
    }
}
