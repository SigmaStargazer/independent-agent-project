using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.1：LLM Agent 配置子面板复制按钮脚本（挂在 PanelLLMAgent 上）。
    /// OnClickCopy：读取「大模型(记忆总结使用)」（Memory 组）的 Base/Key/Model 配置，
    /// 覆盖到当前（Agent）面板的 3 个文本框。只改文本框，不自动保存（走 ESC 既有流程）。
    /// v0.23.5：复制按钮文案本地化（见基类 UILLMCopyPanelBase）。
    /// </summary>
    public class UILLMAgent : UILLMCopyPanelBase
    {
        /// <summary>从「大模型(记忆总结使用)」复制 → 来源面板标题 key = btn_llm_memory_title。</summary>
        protected override string SourceTitleKey => "btn_llm_memory_title";

        /// <summary>
        /// 把 Memory 组配置复制到当前（Agent）面板。
        /// </summary>
        public void OnClickCopy()
        {
            if (mModelConfig == null)
            {
                Debug.LogWarning("[UILLMAgent] mSetting 未绑定，无法复制配置", this);
                return;
            }
            mModelConfig.SetGroup(
                "agent",
                mModelConfig.GetBase("memory"),
                mModelConfig.GetKey("memory"),
                mModelConfig.GetModel("memory"));
            Debug.Log("[UILLMAgent] 已将 Memory 组配置复制到 Agent 面板（未保存，按 ESC 决定是否落盘）", this);
        }
    }
}
