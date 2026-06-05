using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class ActionRuntime
    {
        public string ActionName;
        public ActionState State;
        public string CompleteCondition;
        public Func<bool> CompleteConditionFunc;
        public Func<bool> ErrorConditionFunc;

        // 动作开始位置
        public Vector2 StartPostion;
        // 动作期间位移
        public float Displacement = 0;
        // 动作计时
        public float ActionTime = 0;
        public string StartEnv = "";
        public string EndEnv = "";

        // 跟随动作参数
        public SceneObjBase TargetFollowing;

        // 动作开始时接触的物体集合
        public HashSet<SceneObjBase> StartTouchingObjs = new HashSet<SceneObjBase>();
        // 预计动作过程中允许接触的物体集合
        public HashSet<SceneObjBase> AllowedContactObjs = new HashSet<SceneObjBase>();

        // 用于卡住检测
        public float LastCheckPosX; // 上一次检查的 X 坐标
        public float StuckTime = 0f; // 持续卡住的时间

        public ActionResult Result;
    }

    public class ActionResult
    {
        //public bool Success = true;
        public string Message;   // 给 Agent 用的自然语言描述
    }
}