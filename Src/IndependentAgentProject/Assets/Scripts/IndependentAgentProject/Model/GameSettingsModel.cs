using FrameworkDesign;
using Services;

namespace IndependentAgentProject
{
    /// <summary>
    /// 画面设置数据模型（v0.23.4 MVC 化）。
    /// 纯数据：只存「已保存值」（显示模式下标 + 分辨率下标）。
    /// 配置解析（从 GameSettingsStore 读文件）在 OnInit 里完成（QFramework 惯例）。
    /// 修改经 Command（ChangeDisplayModeCommand / ChangeResolutionCommand），落盘经 SaveGameSettingsCommand。
    /// </summary>
    public interface IGameSettingsModel : IModel
    {
        /// <summary>显示模式下标（0 窗口化 / 1 无边框 / 2 全屏）。</summary>
        BindableProperty<int> DisplayModeIndex { get; }

        /// <summary>分辨率预置列表下标。</summary>
        BindableProperty<int> ResolutionIndex { get; }
    }

    public class GameSettingsModel : AbstractModel, IGameSettingsModel
    {
        public BindableProperty<int> DisplayModeIndex { get; } = new BindableProperty<int>();
        public BindableProperty<int> ResolutionIndex { get; } = new BindableProperty<int>();

        protected override void OnInit()
        {
            var (hasValue, mode, res) = GameSettingsStore.Load();
            // 文件没有任何值时用默认值（全屏 + 1920x1080，Store 已回填），有值时用文件值
            DisplayModeIndex.Value = mode;
            ResolutionIndex.Value = res;
        }
    }
}
