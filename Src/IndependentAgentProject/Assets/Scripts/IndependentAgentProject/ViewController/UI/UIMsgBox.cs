using UnityEngine;
using UnityEngine.UI;

namespace IndependentAgentProject
{
    /// <summary>
    /// 通用 MsgBox 弹窗控制脚本（仅表现层）。
    /// 挂在 MsgBox 根节点（UIMsgBox Prefab）上，负责：
    ///  - 支持 1~3 个按钮，未配置的按钮自动隐藏（未关联 mBtn2 / mBtn3 时）；
    ///  - 对缺失引用给出配置提醒；
    ///  - 弹窗激活时按 ESC 触发 Btn1（默认按钮，通常为「确认」）。
    /// 按钮点击回调、文案由各场景实例在 Inspector 中配置/拖拽绑定，脚本不感知业务。
    /// </summary>
    public class UIMsgBox : MonoBehaviour
    {
        /// <summary>当前是否有任意 UIMsgBox 弹窗处于激活状态（供 UITitle 等查询，避免双重响应 ESC）。</summary>
        public static bool AnyActive { get; private set; }

        [Header("提示文字（场景实例可直接改文案）")]
        [SerializeField]
        private Text mWarningTxt;

        [Header("按钮区（单/双按钮实例：未用的按钮留空，Awake 自动隐藏）")]
        [SerializeField]
        private Button mBtn1;
        [SerializeField]
        private Button mBtn2;
        [SerializeField]
        private Button mBtn3;

        private void Awake()
        {
            if (mWarningTxt == null)
            {
                Debug.LogWarning("[UIMsgBox] 未关联 WarningTxt", this);
            }
            if (mBtn1 == null)
            {
                Debug.LogWarning("[UIMsgBox] 未关联 Btn1", this);
            }

            // 未配置的按钮一律隐藏（支持 1~3 个按钮）
            ApplyButtonVisibility(mBtn2, "Btn2");
            ApplyButtonVisibility(mBtn3, "Btn3");
        }

        private void ApplyButtonVisibility(Button btn, string nodeName)
        {
            if (btn == null)
            {
                Transform node = transform.Find(nodeName);
                if (node != null)
                {
                    node.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 设置提示文案（v0.23.1：结果弹窗显示 API 测试失败原因等动态信息）。
        /// </summary>
        public void SetText(string text)
        {
            if (mWarningTxt != null)
            {
                mWarningTxt.text = text ?? "";
            }
        }

        private void OnEnable()
        {
            transform.SetAsLastSibling();   // 每次显示时移到 Canvas 最上层
            AnyActive = true;               // 弹窗激活，接管 ESC
        }

        private void OnDisable()
        {
            AnyActive = false;              // 弹窗关闭，释放 ESC 接管
        }

        private void Update()
        {
            // 弹窗激活时：ESC → 触发 Btn1（默认按钮）。与项目旧版 Input Manager 一致。
            if (Input.GetButtonDown("Menu"))
            {
                if (mBtn1 != null)
                {
                    mBtn1.onClick.Invoke();
                }
            }
        }
    }
}
