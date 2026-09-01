using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.1：Reranker 配置子面板复制按钮脚本（挂在 PanelReranker 上）。
    /// 背景：Graphiti 的 rerank 是用 LLM 做二分类（OpenAIRerankerClient），
    /// 因此 Reranker 面板常与 Agent 组配相同模型。
    /// OnClickCopy：读取「大模型(智能体使用)」（Agent 组）的 Base/Key/Model 配置，
    /// 覆盖到当前（Reranker）面板的 3 个文本框。只改文本框，不自动保存（走 ESC 既有流程）。
    /// </summary>
    public class UILLMRerank : MonoBehaviour
    {
        [Header("模型配置数据组件（挂 ContentModelConfig 上的 UIModelConfig）")]
        [SerializeField]
        private UIModelConfig mModelConfig;

        /// <summary>
        /// 把 Agent 组配置复制到当前（Reranker）面板。
        /// </summary>
        public void OnClickCopy()
        {
            if (mModelConfig == null)
            {
                Debug.LogWarning("[UILLMRerank] mSetting 未绑定，无法复制配置", this);
                return;
            }
            mModelConfig.SetGroup(
                "reranker",
                mModelConfig.GetBase("agent"),
                mModelConfig.GetKey("agent"),
                mModelConfig.GetModel("agent"));
            Debug.Log("[UILLMRerank] 已将 Agent 组配置复制到 Reranker 面板（未保存，按 ESC 决定是否落盘）", this);
        }
    }
}
