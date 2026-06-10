using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor.U2D.Path.GUIFramework;
using UnityEngine;

namespace IndependentAgentProject
{
    public class RuntimeInfoRenderer
    {
        public string RenderActionSequenceRuntime(ActionSequenceRuntime actionSequenceRuntime, List<SceneObjBase> sceneObjs)
        {
            if (actionSequenceRuntime == null || actionSequenceRuntime.ActionSequence.Count == 0)
            {
                return "无";
            }

            string actionSequenceText = "";
            actionSequenceText += $"## 动作序列状态: {actionSequenceRuntime.State}" +
                $"\n## 动作序列详情: ";
            for (int i = 0; i < actionSequenceRuntime.ActionRuntimeLog.Count; i++)
            {
                var actionRuntime = actionSequenceRuntime.ActionRuntimeLog[i];
                actionSequenceText += $"\n{i}.{RenderActionRuntime(actionRuntime, sceneObjs)} ";
            }
            return actionSequenceText;
        }

        public string RenderActionRuntime(ActionRuntime actionRuntime, List<SceneObjBase> sceneObjs)
        {
            if (actionRuntime == null)
            {
                return "无";
            }
            string text =$"动作名:{actionRuntime.ActionName}\n" +
                $"结束条件:{(string.IsNullOrEmpty(actionRuntime.CompleteCondition) ? "无" : actionRuntime.CompleteCondition)}\n" +
                $"动作状态:{actionRuntime.State}\n";

            if (actionRuntime.TargetFollowing != null)
            {
                int index = sceneObjs.IndexOf(actionRuntime.TargetFollowing);

                if (index >= 0)
                {
                    text += $"跟随目标:{index}. {actionRuntime.TargetFollowing.Name}";
                }
                else
                {
                    text += $"跟随目标:{actionRuntime.TargetFollowing.Name}(目前不在视线内)";
                }
            }
            return text;
        }

        public string RenderObserveRuntimeSummary(List<ObserveRuntime> observeRuntimes, List<SceneObjBase> sceneObjs)
        {
            if (observeRuntimes.Count == 0)
            {
                return "无";
            }

            List<string> infos = new();
            int num = 1;
            foreach (var runtime in observeRuntimes)
            {
                if (runtime.Target == null)
                    continue;
                
                int index = sceneObjs.IndexOf(runtime.Target);
                if (index >= 0)
                {
                    var curTime = Time.time;
                    var elapsed = curTime - runtime.LastChangeTime;
                    var observeTime = curTime - runtime.ObserveStartTime;
                    infos.Add($"观察目标[{num}]\n" +
                        $"对象: {index}. {runtime.TargetName}\n" +
                        $"观察时长:{observeTime:F1}秒\n" +
                        $"最后状态: {runtime.LastStateName}\n" +
                        $"最后变化: {elapsed:F1}秒前\n" +
                        $"状态变化次数:{runtime.StateChangeNum}次\n" +
                        $"未读记录: {runtime.UnreadCount}条\n" +
                        $"存储记录: {runtime.Records.Count}条");
                }
                else
                {
                    infos.Add($"观察目标[{num}]:\n" +
                        $"对象: {runtime.TargetName}(目前不在视线内)");
                }
                num++;
            }
            return $"目前正对{observeRuntimes.Count}个目标进行持续观察\n" +
                string.Join("\n\n", infos);
        }

        public string RenderObserveTargetRuntime(ObserveRuntime runtime)
        {
            var curTime = Time.time;
            var elapsed = curTime - runtime.LastChangeTime;
            string elapsedKey = runtime.StateChangeNum == 0 ? $"距离开始观察" : $"距离上次状态改变";
            var observeTime = curTime - runtime.ObserveStartTime;
            string text =
                $"[观察记录]\n" +
                $"对象:{runtime.TargetName}\n" +
                $"观察时长:{observeTime:F1}秒\n" +
                $"最后状态:{runtime.LastStateName}\n" +
                $"{elapsedKey}:{elapsed:F1}秒前\n" +
                $"存储记录: {runtime.Records.Count}条\n\n";
            int idx = 1;
            foreach (string record in runtime.Records)
            {
                text += $"==========记录{idx}==========\n";
                text += record;
                text += "\n\n";
                idx++;
            }
            return text;
        }

        public string RenderTimerSummary(List<TimerRuntime> timerRuntimes)
        {
            if (timerRuntimes.Count == 0)
            {
                return "无";
            }

            List<string> lines = new();
            foreach (var timer in timerRuntimes)
            {
                string repeatText = timer.TimerRepeat ? "是" : "否";
                lines.Add(
                    $"timer_id:{timer.TimerId}\n" +
                    $"名称:{timer.TimerName}\n" +
                    $"剩余:{timer.RemainingSeconds:F1}秒\n" +
                    $"重复:{repeatText}"
                );
            }

            return string.Join("\n\n", lines);
        }

        public string RenderTimerListDetail(List<TimerRuntime> timerRuntimes)
        {
            if (timerRuntimes.Count == 0)
            {
                return "[定时器列表] 当前没有进行中的定时器";
            }

            List<string> lines = new() { "[定时器列表]" };
            int index = 1;
            foreach (var timer in timerRuntimes)
            {
                string repeatText = timer.TimerRepeat ? "是" : "否";
                lines.Add(
                    $"{index}. 定时器id:{timer.TimerId}\n" +
                    $"名称:{timer.TimerName}\n" +
                    $"描述:{timer.TimerDescription}\n" +
                    $"剩余:{timer.RemainingSeconds:F1}秒\n" +
                    $"重复:{repeatText}"
                );
                index++;
            }

            return string.Join("\n\n", lines);
        }

        #region WorldEventLog 渲染

        public string FormatSceneObjLabel(SceneObjBase obj, List<SceneObjBase> sceneObjs)
        {
            if (obj == null)
                return "Unknown";

            int index = sceneObjs.IndexOf(obj);
            if (index >= 0)
                return $"{index}. {obj.Name}";

            return $"{obj.Name}(目前不在环境列表内)";
        }

        public string BuildIndexChangeNotice(SceneObjBase obj, string newState, List<SceneObjBase> sceneObjs)
        {
            if (obj == null)
                return string.Empty;

            if (newState == "Appearance")
            {
                int index = sceneObjs.IndexOf(obj);
                if (index < 0)
                    return "[索引变化]\n新出现物体: 无法解析索引";

                return $"[索引变化]\n新出现物体: {index}. {obj.Name}（加入环境列表）\n其余物体索引未变";
            }

            if (newState == "Disappearance")
            {
                int removedIndex = sceneObjs.IndexOf(obj);
                if (removedIndex < 0)
                    return $"[索引变化]\n消失物体: {obj.Name}（已从环境列表移除）";

                var lines = new List<string>
                {
                    "[索引变化]",
                    $"消失物体: {removedIndex}. {obj.Name}（已从环境列表移除）"
                };

                if (removedIndex + 1 >= sceneObjs.Count)
                {
                    lines.Add("无其余物体索引变化");
                }
                else
                {
                    lines.Add("以下物体索引前移:");
                    for (int i = removedIndex + 1; i < sceneObjs.Count; i++)
                        lines.Add($"  原 {i}. {sceneObjs[i].Name} -> 现 {i - 1}. {sceneObjs[i].Name}");
                }

                return string.Join("\n", lines);
            }

            return string.Empty;
        }

        /// <summary>
        /// 构建场景对象世界事件的 msg 正文（不含 CreateMessageText 包裹）
        /// </summary>
        public string BuildSceneObjEventMsg(SceneObjBase obj, string oldState, string newState, List<SceneObjBase> sceneObjs)
        {
            string label = FormatSceneObjLabel(obj, sceneObjs);
            string msg = $"[世界事件]对象:{label} 状态:{oldState} -> {newState}";

            if (newState == "Appearance" || newState == "Disappearance")
            {
                string notice = BuildIndexChangeNotice(obj, newState, sceneObjs);
                if (!string.IsNullOrEmpty(notice))
                    msg += "\n\n" + notice;
            }

            return msg;
        }

        /// <summary>
        /// 构建 Agent 自身世界事件的 msg 正文（不含 CreateMessageText 包裹）
        /// </summary>
        public string BuildSelfEventMsg(string agentName, string oldState, string newState)
        {
            return $"[世界事件]对象:{agentName} 状态:{oldState} -> {newState}";
        }

        /// <summary>
        /// 渲染完整的世界事件日志输出文本
        /// </summary>
        public string RenderWorldEventLog(Queue<WorldEventRecord> worldEventLog)
        {
            float now = Time.time;
            var sb = new StringBuilder();
            sb.AppendLine("[世界事件记录]");
            sb.AppendLine($"总记录数: {worldEventLog.Count}");

            int idx = 1;
            foreach (var record in worldEventLog)
            {
                float elapsed = now - record.Time;
                sb.AppendLine();
                sb.AppendLine($"==========事件{idx}==========");
                sb.AppendLine($"时间: {elapsed:F1}秒前");
                sb.AppendLine(record.EventText);
                idx++;
            }

            return sb.ToString();
        }

        #endregion
    }
}
