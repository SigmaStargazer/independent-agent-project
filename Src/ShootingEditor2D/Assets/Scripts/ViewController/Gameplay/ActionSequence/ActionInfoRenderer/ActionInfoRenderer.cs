using System.Collections;
using System.Collections.Generic;
using UnityEditor.U2D.Path.GUIFramework;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ActionInfoRenderer
    {
        public string RenderActionSequenceRuntime(ActionSequenceRuntime actionSequenceRuntime)
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
                actionSequenceText += $"\n{i}.{RenderActionRuntime(actionRuntime)} ";
            }
            return actionSequenceText;
        }

        public string RenderActionRuntime(ActionRuntime actionRuntime)
        {
            if (actionRuntime == null)
            {
                return "无";
            }
            return $"动作名:{actionRuntime.ActionName} " +
                $"结束条件:{actionRuntime.CompleteCondition} " +
                $"动作状态:{actionRuntime.State} ";
        }
    }
}
