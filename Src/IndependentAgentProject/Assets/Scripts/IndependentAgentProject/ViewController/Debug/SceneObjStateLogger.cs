using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace IndependentAgentProject.DebugTools
{
    /// <summary>
    /// 调试组件：订阅指定 <see cref="SceneObjBase"/> 的 <see cref="SceneObjBase.OnStateChanged"/> 事件，
    /// 把每次状态切换追加到 <see cref="TMP_Text"/>,形成时序日志,便于人工复盘状态机跳转路径。
    /// 纯调试用途,不依赖任何业务逻辑;不挂载即不生效,移除后零副作用。
    /// </summary>
    public class SceneObjStateLogger : MonoBehaviour
    {
        [SerializeField][Tooltip("状态历史要写入的文本域(UI 或 3D TMP_Text 均可)。")]
        private TMP_Text targetTextField;

        [SerializeField][Tooltip("要监听的 SceneObj。")]
        private SceneObjBase target;

        [SerializeField][Tooltip("文本域最多保留的行数,超出后丢弃最旧行。")]
        private int maxLines = 30;

        [SerializeField][Tooltip("每行是否带 Time.time。")]
        private bool includeTimestamp = true;

        [SerializeField][Tooltip("是否同时 Debug.Log 同样字符串。")]
        private bool logToConsole = false;

        private readonly List<string> lines = new List<string>();

        private void OnEnable()
        {
            if (target == null)
            {
                AppendLine("[error] Target 未赋值");
                enabled = false;
                return;
            }
            target.OnStateChanged += HandleStateChanged;
            // 写入基线:订阅时 Target 已有的当前状态。
            AppendLine($"[init] {target.GetStateName()}");
        }

        private void OnDisable()
        {
            if (target != null)
                target.OnStateChanged -= HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (target != null)
                target.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(SceneObjBase obj, string oldState, string newState)
        {
            string ts = includeTimestamp ? $"[{Time.time:F2}] " : "";
            AppendLine($"{ts}{obj.Name}: {oldState} -> {newState}");
        }

        private void AppendLine(string line)
        {
            if (targetTextField == null) return;
            lines.Add(line);
            int overflow = lines.Count - maxLines;
            if (overflow > 0) lines.RemoveRange(0, overflow);
            targetTextField.text = string.Join("\n", lines);
            if (logToConsole) Debug.Log(line);
        }

        /// <summary>
        /// 清空文本域与内部行缓存。可被其他调试按钮调用。
        /// </summary>
        public void Clear()
        {
            lines.Clear();
            if (targetTextField != null) targetTextField.text = "";
        }
    }
}
