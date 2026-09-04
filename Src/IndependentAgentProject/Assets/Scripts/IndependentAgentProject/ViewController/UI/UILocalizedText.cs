using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IndependentAgentProject
{
    /// <summary>
    /// 静态文本本地化绑定组件（v0.23.5）。
    /// 挂在任意 Text / TMP_Text 节点上，在 Inspector 填 <see cref="mKey"/>（与 Excel「全部文案」key 列一致），
    /// Awake 注册语言变更事件并按当前语言填文案；语言切换时自动刷新。
    /// mKey 为空时不动（保持场景手动配置的文案），与 UITextProvider 既有语义一致。
    /// 只处理静态文本；动态文本（运行时由代码 SetText 的）不应挂本组件，应由代码在语言事件里重新 Get。
    /// </summary>
    public class UILocalizedText : MonoBehaviour
    {
        [Header("文案 key（与 Excel「全部文案」key 列一致；留空 = 不处理）")]
        [SerializeField]
        private string mKey;

        [Header("目标组件（不填则从自身组件自动获取）")]
        [SerializeField]
        private Text mText;         // UI.Text (Legacy)
        [SerializeField]
        private TMP_Text mTmpText;  // TextMeshPro

        private void Awake()
        {
            if (mText == null && mTmpText == null)
            {
                mText = GetComponent<Text>();
                if (mText == null)
                {
                    mTmpText = GetComponent<TMP_Text>();
                }
            }
            if (mText == null && mTmpText == null)
            {
                Debug.LogWarning($"[UILocalizedText] {name} 未找到 Text/TMP_Text 组件，无法本地化", this);
                return;
            }
            if (string.IsNullOrEmpty(mKey))
            {
                return;   // 无 key：保持场景手动文案
            }
            UITextProvider.RegisterLanguageChanged(Refresh);
            Refresh();
        }

        private void OnDestroy()
        {
            UITextProvider.UnregisterLanguageChanged(Refresh);
        }

        /// <summary>按 mKey 从 UITextProvider 取文案写入目标组件。</summary>
        public void Refresh()
        {
            if (string.IsNullOrEmpty(mKey))
            {
                return;
            }
            string text = UITextProvider.Get(mKey);
            if (mText != null)
            {
                mText.text = text;
            }
            if (mTmpText != null)
            {
                mTmpText.text = text;
            }
        }
    }
}
