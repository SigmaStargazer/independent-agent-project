using System;
using UnityEngine;

namespace IndependentAgentProject
{
    public class ObserveRuntime
    {
        public string ActionName;
        public ActionState State;
        public SceneObjBase Target;
        public string LastStateName;
        public Action<SceneObjBase, string, string> StateChangedHandler;
    }
}