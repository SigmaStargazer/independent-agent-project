using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 「按任意按钮」面板的文字闪烁（呼吸）提示。
    /// 只负责本面板自身表现；面板切换由 UITitle 总控。
    /// 挂在 PanelPressAnyButton 上，需为提示文字挂 CanvasGroup 并关联到 mHintGroup。
    /// </summary>
    public class UIPressAnyButton : MonoBehaviour
    {
        [Header("闪烁提示文字（CanvasGroup）")]
        [SerializeField]
        private CanvasGroup mHintGroup;
        [Tooltip("呼吸周期（秒），数值越大闪得越慢")]
        [SerializeField]
        private float mBlinkTime = 2f;
        [Tooltip("呼吸最暗 Alpha")]
        [SerializeField]
        private float mMinAlpha = 0.15f;
        [Tooltip("呼吸最亮 Alpha")]
        [SerializeField]
        private float mMaxAlpha = 1f;

        private void Update()
        {
            // 仅当本面板激活时呼吸；面板被 UITitle 关闭后自然停止
            if (mHintGroup == null || !gameObject.activeSelf)
            {
                return;
            }

            float t = (Mathf.Sin(Time.time * Mathf.PI * 2f / mBlinkTime) + 1f) * 0.5f;
            mHintGroup.alpha = Mathf.Lerp(mMinAlpha, mMaxAlpha, t);
        }
    }
}
