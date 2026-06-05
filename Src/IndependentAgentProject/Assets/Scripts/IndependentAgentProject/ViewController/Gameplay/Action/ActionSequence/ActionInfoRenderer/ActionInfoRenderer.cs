using System.Collections;
using System.Collections.Generic;
using UnityEditor.U2D.Path.GUIFramework;
using UnityEngine;

namespace IndependentAgentProject
{
    public class ActionInfoRenderer
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

        public string RenderObserveRuntime(List<ObserveRuntime> observeRuntimes, List<SceneObjBase> sceneObjs)
        {
            if (observeRuntimes.Count == 0)
            {
                return "无";
            }

            List<string> infos = new();
            foreach (var runtime in observeRuntimes)
            {
                if (runtime.Target == null)
                    continue;

                int index = sceneObjs.IndexOf(runtime.Target);

                if (index >= 0)
                {
                    infos.Add($"- {index}. {runtime.Target.Name}");
                }
                else
                {
                    infos.Add($"- {runtime.Target.Name}(目前不在视线内)");
                }
            }
            return $"目前正对{observeRuntimes.Count}个目标进行持续观察\n" +
                string.Join("\n", infos);
        }
    }
}
