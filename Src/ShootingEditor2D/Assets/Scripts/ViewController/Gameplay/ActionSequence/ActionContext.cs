using System;
using System.Numerics;

namespace ShootingEditor2D
{
    public class ActionContext
    {
        public string ActionName;
        public Func<bool> EndCondition;
        // 动作开始位置
        public Vector2 startPostion;
        // 通用计时
        public float ActionTime;

        public ActionResult Result;
    }

    public class ActionResult
    {
        public string ActionName;
        public bool Success = true;
        public string Message;   // 给 Agent 用的自然语言描述
    }
}