using DynamicExpresso;
using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using System.Text.RegularExpressions;

namespace IndependentAgentProject
{
    public enum ConditionEvalStatus
    {
        True,     // 条件成立
        False,    // 条件未成立（正常情况）
        Error     // 表达式错误 / 语义错误 / 系统异常
    }
    public class ConditionEvalResult
    {
        public ConditionEvalStatus Status;
        public string ErrorMessage;   // 仅 Error 时有效
    }
    public class ConditionEvaluator
    {
        private Interpreter mInterpreter;

        public ConditionEvaluator()
        {
            mInterpreter = new Interpreter(InterpreterOptions.Default);
            mInterpreter.Reference(typeof(Math)); // 可扩展
            mInterpreter.SetFunction("Distance", (Func<Vector2, Vector2, float>)((a, b) => a.x-b.x));
        }

        /// <summary>
        /// Validate 在动作规划阶段调用
        /// </summary>
        public List<ConditionEvalResult> ValidateAll(List<ActionStep> actionSequence, ConditionContext context)
        {
            List<ConditionEvalResult> results = new List<ConditionEvalResult>();
            if (actionSequence == null || actionSequence.Count == 0)
                return results;

            int index = 0;
            foreach (var step in actionSequence)
            {
                var result = new ConditionEvalResult();
                if (string.IsNullOrWhiteSpace(step.Condition))
                {
                    result.Status = ConditionEvalStatus.True;
                }
                else
                {
                    SetVariables(context);

                    try
                    {
                        var semanticCheck = ValidateNearestInteractableIndexReference(step.Condition, context);
                        if (semanticCheck != null)
                        {
                            results.Add(semanticCheck);
                            index++;
                            continue;
                        }

                        bool ok = mInterpreter.Eval<bool>(step.Condition);
                        result.Status = ok ? ConditionEvalStatus.True : ConditionEvalStatus.False;
                    }
                    catch (Exception e)
                    {
                        result.Status = ConditionEvalStatus.Error;
                        result.ErrorMessage = $"action_sequence[{index}].condition校验出错: {e.Message}";
                    }
                }
                results.Add(result);
                index++;
            }
            return results;
        }

        /// <summary>
        /// Evaluate 在动作执行阶段调用
        /// </summary>
        public ConditionEvalResult Evaluate(int index, ActionStep step, ConditionContext context)
        {
            var result = new ConditionEvalResult();
            if (step == null || string.IsNullOrWhiteSpace(step.Condition))
            {
                result.Status = ConditionEvalStatus.True;
                return result;
            }

            // 投影视图刷新
            context.RefreshViews();
            SetVariables(context);

            try
            {
                bool ok = mInterpreter.Eval<bool>(step.Condition);
                result.Status = ok ? ConditionEvalStatus.True : ConditionEvalStatus.False;
            }
            catch (Exception e)
            {
                result.Status = ConditionEvalStatus.Error;
                result.ErrorMessage = $"action_sequence[{index}].condition校验出错: {e.Message}";
            }
            return result;
        }

        /// <summary>
        /// 每次 Evaluate / Validate 时更新 interpreter 变量
        /// </summary>
        private void SetVariables(ConditionContext context)
        {
            mInterpreter.SetVariable("myself", context.Myself);
            mInterpreter.SetVariable("objects", context.Objects);
            mInterpreter.SetVariable("displacement", context.Displacement);
            mInterpreter.SetVariable("actionTime", context.ActionTime);
            mInterpreter.SetVariable("canInteract", context.CanInteract);
            mInterpreter.SetVariable("nearestInteractableIndex", context.NearestInteractableIndex);
        }

        private ConditionEvalResult ValidateNearestInteractableIndexReference(string condition, ConditionContext context)
        {
            if (string.IsNullOrWhiteSpace(condition))
                return null;

            // 匹配:
            // nearestInteractableIndex == 7
            // nearestInteractableIndex!=3
            // nearestInteractableIndex == 12
            var matches = Regex.Matches(condition,
                @"nearestInteractableIndex\s*(==|!=)\s*(\d+)");

            foreach (Match match in matches)
            {
                int objIndex;
                objIndex = int.Parse(match.Groups[2].Value);
                // Index越界
                if (objIndex < 0 || objIndex >= context.ObjectsSrc.Count)
                {
                    return new ConditionEvalResult
                    {
                        Status = ConditionEvalStatus.Error,
                        ErrorMessage = $"nearestInteractableIndex引用了不存在的objects[{objIndex}]"
                    };
                }

                var obj = context.ObjectsSrc[objIndex];
                if (obj is not IInteractable interactable)
                {
                    return new ConditionEvalResult
                    {
                        Status = ConditionEvalStatus.Error,
                        ErrorMessage =$"objects[{objIndex}]({obj.Name})无法交互，不能用于nearestInteractableIndex判断。" +
                        $"nearestInteractableIndex只能引用可交互对象。若目标是平台、墙体、悬崖等场景物体，请改用displacement或objects[{objIndex}].Position相关条件。"
                    };
                }
                if (!interactable.IsInteractable)
                {
                    return new ConditionEvalResult
                    {
                        Status = ConditionEvalStatus.Error,
                        ErrorMessage = $"objects[{objIndex}]({obj.Name})无法交互，不能用于nearestInteractableIndex判断。" +
                        $"nearestInteractableIndex只能引用可交互对象。若目标是平台、墙体、悬崖等场景物体，请改用displacement或objects[{objIndex}].Position相关条件。"
                    };
                }
            }

            return null;
        }
    }
}

