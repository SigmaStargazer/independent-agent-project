using TMPro;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// v0.23.5：模型配置复制按钮文案本地化基类（挂各配置子面板，如 PanelLLMAgent）。
    /// 复制按钮文本「把 「{来源}」 的配置复制到此处」是带变量（来源面板名）的动态文案，
    /// 语言切换时需按 key 重新拼接，因此由代码在语言事件里刷新，而非静态 UILocalizedText。
    /// 子类只需提供 <see cref="SourceTitleKey"/>（来源面板标题 key）。
    /// </summary>
    public abstract class UILLMCopyPanelBase : MonoBehaviour
    {
        [Header("模型配置数据组件（挂 ContentModelConfig 上的 UIModelConfig）")]
        [SerializeField]
        protected UIModelConfig mModelConfig;

        [Header("复制按钮文本（BtnCopy/Text (TMP)，语言切换自动刷新）")]
        [SerializeField]
        protected TMP_Text mCopyText;

        /// <summary>来源面板标题 key（如 btn_llm_agent_title / btn_llm_memory_title），由子类定义。</summary>
        protected abstract string SourceTitleKey { get; }

        private void Awake()
        {
            UITextProvider.RegisterLanguageChanged(RefreshCopyText);
            RefreshCopyText();
        }

        private void OnDestroy()
        {
            UITextProvider.UnregisterLanguageChanged(RefreshCopyText);
        }

        /// <summary>按「把 「{来源}」 的配置复制到此处」重新拼接复制按钮文案。</summary>
        protected void RefreshCopyText()
        {
            if (mCopyText == null)
            {
                return;
            }
            string title = UITextProvider.Get(SourceTitleKey);
            mCopyText.text = UITextProvider.Get("config_copy_from", title);
        }
    }
}
