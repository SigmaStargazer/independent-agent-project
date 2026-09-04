using FrameworkDesign;

namespace IndependentAgentProject
{
    /// <summary>
    /// 修改语言（v0.23.5）。
    /// View（UISetting）点左右箭头 → SendCommand → 改 Model.Language → BindableProperty 事件驱动 UI 刷新。
    /// 语言变更的连锁反应（UITextProvider 换表 + 全局刷新）由 Model 的 Language 订阅回调触发（见 UISetting），
    /// 本 Command 只负责写值，保持与 ChangeDisplayModeCommand 一致的分层。
    /// </summary>
    public class ChangeLanguageCommand : AbstractCommand
    {
        private readonly int mDelta;   // +1 下一语言 / -1 上一语言（仅两个语言，来回切）

        public ChangeLanguageCommand(int delta)
        {
            mDelta = delta;
        }

        protected override void OnExecute()
        {
            var model = this.GetModel<IGameSettingsModel>();
            int count = 2;   // 语言数量（UITextLanguage 枚举成员数）
            int next = ((model.Language.Value + mDelta) % count + count) % count;
            model.Language.Value = next;   // 越界循环，无需 View 限制
        }
    }
}
