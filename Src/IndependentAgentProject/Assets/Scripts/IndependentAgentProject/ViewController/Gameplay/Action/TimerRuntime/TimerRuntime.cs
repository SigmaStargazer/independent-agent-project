using UnityEngine;

namespace IndependentAgentProject
{
    public class TimerRuntime
    {
        public int TimerId;
        public string TimerName;
        public string TimerDescription;
        public float DelaySeconds;
        public bool TimerRepeat;
        public float StartTime;
        public float TriggerTime;

        public float RemainingSeconds => Mathf.Max(0f, TriggerTime - Time.time);
    }
}
