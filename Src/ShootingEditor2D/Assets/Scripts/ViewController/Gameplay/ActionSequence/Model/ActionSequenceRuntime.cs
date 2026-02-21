using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public enum ActionSequenceState
    {
        Idle,           // 空闲
        //Validating,     // 校验中
        //WaitingConfirm, // 等待确认
        Executing,      // 执行中
        Paused,         // 暂停中
        Completed,      // 已完成
        Aborted         // 已中止
    }
    public class ActionSequenceRuntime
    {
        public ActionSequenceState State = ActionSequenceState.Idle;
        public List<ActionStep> ActionSequence;
        public int CurActionIndex = 0;
        public List<DeviceBase> DeviceSnap = new List<DeviceBase>();
        public List<ActionRuntime> ActionRuntimeLog = new List<ActionRuntime>();

        public void AddDevice(DeviceBase device)
        {
            DeviceSnap.Add(device);
        }

        public void AddActionRuntimeLog(ActionRuntime actionRuntime)
        {
            ActionRuntimeLog.Add(actionRuntime);
        }

        /// <summary>
        /// 判断是否还有ActionStep，然后返回ActionStep/null
        /// </summary>
        public ActionStep GetCurActionStep()
        {
            if (CurActionIndex < ActionSequence.Count)
            {
                return ActionSequence[CurActionIndex];
            }
            else
                return null;
        }

        // 内存释放
        public void Dispose()
        {
            // ===== 状态标记 =====
            State = ActionSequenceState.Aborted;

            // ===== 释放内部 runtime 对象 =====
            if (ActionRuntimeLog != null)
            {
                foreach (var rt in ActionRuntimeLog)
                {
                    if (rt is IDisposable d)
                        d.Dispose();
                }
                ActionRuntimeLog.Clear();
                ActionRuntimeLog = null;
            }

            // ===== 释放设备快照 =====
            if (DeviceSnap != null)
            {
                DeviceSnap.Clear();
                DeviceSnap = null;
            }

            // ===== 释放动作序列 =====
            if (ActionSequence != null)
            {
                ActionSequence.Clear();
                ActionSequence = null;
            }

            //// ===== 释放策略 / 服务对象 =====
            //if (mConditionEvaluator != null)
            //{
            //    if (mConditionEvaluator is IDisposable d)
            //        d.Dispose();

            //    mConditionEvaluator = null;
            //}

            // ===== 运行态索引复位 =====
            CurActionIndex = 0;
        }
    }
}


