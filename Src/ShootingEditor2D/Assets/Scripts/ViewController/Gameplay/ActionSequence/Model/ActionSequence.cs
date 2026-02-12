using SkillBridge.Message;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public enum ActionNodeState
    {
        Todo,
        Doing,
        Done,
        Failed,
        Aborted
    }

    public class ActionNode
    {
        public ActionStep Proto;
        public ActionNodeState State;
        public ActionContext Context;
    }
    public class ActionSequence
    {
        public string SequenceId;
        public List<ActionNode> Nodes = new();
        public int Cursor = 0;
        public SceneObjSnapshot Snapshot;

        public bool IsRunning;
        public bool IsAborted;

        public ActionSequenceLog Log = new();
    }
}
