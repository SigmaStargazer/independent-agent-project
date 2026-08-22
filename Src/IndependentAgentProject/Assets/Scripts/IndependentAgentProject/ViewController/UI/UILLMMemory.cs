using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.1：LLM Memory 配置子面板复制按钮脚本（挂在 PanelLLMMemory 上）。
    /// OnClickCopy：读取「大模型(智能体使用)」（Agent 组）的 Base/Key/Model 配置，
    /// 覆盖到当前（Memory）面板的 3 个文本框。只改文本框，不自动保存（走 ESC 既有流程）。
    /// </summary>
    public class UILLMMemory : MonoBehaviour
    {
        [Header("API 配置读写组件（挂 UIConfig 上的 UISetting）")]
        [SerializeField]
        private UISetting mSetting;

        /// <summary>
        /// 把 Agent 组配置复制到当前（Memory）面板。
        /// </summary>
        public void OnClickCopy()
        {
            if (mSetting == null)
            {
                Debug.LogWarning("[UILLMMemory] mSetting 未绑定，无法复制配置", this);
                return;
            }
            mSetting.SetGroup(
                "memory",
                mSetting.GetBase("agent"),
                mSetting.GetKey("agent"),
                mSetting.GetModel("agent"));
            Debug.Log("[UILLMMemory] 已将 Agent 组配置复制到 Memory 面板（未保存，按 ESC 决定是否落盘）", this);
        }
    }
}
