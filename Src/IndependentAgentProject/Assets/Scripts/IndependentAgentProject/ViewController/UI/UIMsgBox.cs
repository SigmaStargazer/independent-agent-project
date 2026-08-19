using UnityEngine;
using UnityEngine.UI;

namespace IndependentAgentProject
{
    /// <summary>
    /// 通用 MsgBox 弹窗控制脚本（仅表现层）。
    /// 挂在 MsgBox 根节点（UIMsgBox Prefab）上，负责：
    ///  - 单按钮实例自动隐藏第 2 个按钮（未关联 mBtn2 时）；
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

        [Header("按钮区（单按钮实例：Btn2 留空即可，Awake 自动隐藏）")]
        [SerializeField]
        private Button mBtn1;
        [SerializeField]
        private Button mBtn2;

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

            // 单按钮实例：未关联 mBtn2 时，自动隐藏模板默认的第 2 个按钮节点
            if (mBtn2 == null)
            {
                Transform btn2 = transform.Find("Btn2");
                if (btn2 != null)
                {
                    btn2.gameObject.SetActive(false);
                }
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
