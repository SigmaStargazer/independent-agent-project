using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ActionSequenceLog
    {
        public List<ActionLogEntry> Entries = new();
        public bool Finished;
        public string AbortReason;
    }

    public class ActionLogEntry
    {
        public int Index;
        public string ActionName;
        public ActionNodeState State;

        public string StartEnv;
        public string EndEnv;
    }

}