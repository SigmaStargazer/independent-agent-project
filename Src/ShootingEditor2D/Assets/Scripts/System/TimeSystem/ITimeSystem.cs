using System;
using System.Collections.Generic;
using FrameworkDesign;
using UnityEngine;

namespace ShootingEditor2D
{
    public interface ITimeSystem : ISystem
    {
        float CurSeconds { get; }
        void AddDelayTask(float seconds, Action onDelayFinish);
    }

    public enum DelayTaskState
    {
        NotStart,
        Started,
        Finish
    }

    public class DelayTask
    {
        public float Seconds { get; set; }
        public Action OnFinish { get; set; }
        public float StartSeconds { get; set; }
        public float FinishSeconds { get; set; }
        public DelayTaskState State { get; set; }

    }

    public class TimeSystem : AbstractSystem, ITimeSystem
    {
        public class TimeSystemUpadteBehaviour : MonoBehaviour
        {
            public event Action OnUpdate;

            private void Update()
            {
                OnUpdate?.Invoke();
            }
        }
        protected override void OnInit()
        {
            var updateBehaviourGameObj = new GameObject(nameof(TimeSystemUpadteBehaviour));
            UnityEngine.Object.DontDestroyOnLoad(updateBehaviourGameObj);

            // 如果需要销毁，可以缓存为成员变量
            var updateBehaviour = updateBehaviourGameObj.AddComponent<TimeSystemUpadteBehaviour>();
            updateBehaviour.OnUpdate += OnUpdate;
        }

        public float CurSeconds { get; private set; } = 0.0f;

        // 双向链表
        private LinkedList<DelayTask> mDelayTasks = new LinkedList<DelayTask>();

        private void OnUpdate()
        {
            CurSeconds += Time.deltaTime;

            if (mDelayTasks.Count > 0)
            {
                var curNode = mDelayTasks.First;
                while (curNode != null)
                {
                    var delayTask = curNode.Value;
                    var nextTask = curNode.Next;
                    if (delayTask.State == DelayTaskState.NotStart)
                    {
                        delayTask.State = DelayTaskState.Started;
                        delayTask.StartSeconds = CurSeconds;
                        delayTask.FinishSeconds = CurSeconds + delayTask.Seconds;
                    }
                    else if(delayTask.State == DelayTaskState.Started)
                    {
                        if(CurSeconds > delayTask.FinishSeconds)
                        {
                            delayTask.State = DelayTaskState.Finish;
                            delayTask.OnFinish?.Invoke();
                            delayTask.OnFinish = null;
                            mDelayTasks.Remove(curNode);// 删除节点
                        }
                    }
                    curNode = nextTask;
                }
            }
        }

        public void AddDelayTask(float seconds, Action onFinish)
        {
            var delayTask = new DelayTask()
            {
                Seconds = seconds,
                OnFinish = onFinish,
                State = DelayTaskState.NotStart
            };
            mDelayTasks.AddLast(new LinkedListNode<DelayTask>(delayTask));
        }
    }
}

