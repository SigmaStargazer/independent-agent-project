using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public enum ActionState
    {
        Todo,
        Doing,
        Done,
        Failed,
        Aborted
    }
    public class ActionRuntime
    {
        public string ActionName;
        public string CompleteCondition;
        public Func<bool> CompleteConditionFunc;
        public Func<bool> ErrorConditionFunc;
        public ActionState State;

        // 动作开始位置
        public Vector2 StartPostion;
        // 动作期间位移
        public float Displacement = 0;
        // 动作计时
        public float ActionTime = 0;
        public string StartEnv = "";
        public string EndEnv = "";

        // 动作开始时接触的物体集合
        public HashSet<SceneObjBase> StartTouchingObjs = new HashSet<SceneObjBase>();

        public ActionResult Result;
    }

    public class ActionResult
    {
        //public bool Success = true;
        public string Message;   // 给 Agent 用的自然语言描述
    }
}