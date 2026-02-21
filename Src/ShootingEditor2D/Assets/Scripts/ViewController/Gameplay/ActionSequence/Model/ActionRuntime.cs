using System;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ActionRuntime
    {
        public string ActionName;
        public Func<bool> CompleteConditionFunc;
        public string CompleteCondition;
        // 动作开始位置
        public Vector2 StartPostion;
        // 动作期间位移
        public float Displacement = 0;
        // 动作计时
        public float ActionTime = 0;
        public string StartEnv;
        public string EndEnv;

        public ActionResult Result;
    }

    public class ActionResult
    {
        public bool Success = true;
        public string Message;   // 给 Agent 用的自然语言描述
    }
}