using FrameworkDesign;
using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class ActionSequenceManager : MonoSingleton<ActionSequenceManager>
    {
        private ActionSequence current;

        public void ConfirmAndStart(AgentActionSequenceRequest req, Agent agent)
        {
            //AbortCurrent("override by new sequence");


            //var snap = DeviceManager.Instance.BuildSnapshot();


            //var instance = new ActionSequence
            //{
            //    SequenceId = Guid.NewGuid().ToString(),
            //    Snapshot = snap,
            //    IsRunning = true
            //};


            //foreach (var step in req.ActionSequences)
            //{
            //    instance.Nodes.Add(new ActionNode
            //    {
            //        Proto = step,
            //        State = ActionNodeState.Todo
            //    });
            //}


            //current = instance;
            //StartNext(agent);
        }

        void StartNext(Agent agent)
        {
            if (current == null || current.Cursor >= current.Nodes.Count)
            {
                Finish();
                return;
            }


            var node = current.Nodes[current.Cursor];
            node.State = ActionNodeState.Doing;


            var ctx = ActionExecutor.Build(node.Proto, current.Snapshot, agent);
            node.Context = ctx;


            //agent.SetAction(ctx, () =>
            //{
            //    node.State = ActionNodeState.Done;
            //    current.Cursor++;
            //    StartNext(agent);
            //});
        }


        public void AbortCurrent(string reason)
        {
            if (current == null) return;
            current.IsAborted = true;
            current.Log.AbortReason = reason;
            current = null;
        }


        void Finish()
        {
            if (current == null) return;
            current.Log.Finished = true;
            Debug.Log("[ActionSequence] Finished");
            current = null;
        }
    }

}
