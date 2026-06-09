using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class ObserveRuntime
    {
        public string ActionName;
        public ActionState State;
        public SceneObjBase Target;
        public string TargetName; // 防止destroy后无法获取Target.Name

        /// <summary>
        /// 最近的观察记录
        /// </summary>
        public const int MaxRecords = 20; // 最大记录数
        public Queue<string> Records = new(); // 观察记录
        public int UnreadCount;// 自上次Agent读取后新增的记录数

        public float ObserveStartTime;

        public string LastStateName;
        public float LastChangeTime; // 最后变化时间
        public int StateChangeNum = 0; // 状态变化次数
        public Action<SceneObjBase, string, string> StateChangedHandler;
    }
}