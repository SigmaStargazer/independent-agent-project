using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameFlow
{
    public interface IGameFlow
    {
        bool ShowLoadingScreen { get; }
        FlowFailPolicy FailPolicy { get; }
        IReadOnlyList<IFlowStep> Steps { get; }
        string TargetScene { get; }
    }
}
