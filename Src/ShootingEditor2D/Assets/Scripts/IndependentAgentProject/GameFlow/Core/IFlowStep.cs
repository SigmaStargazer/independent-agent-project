using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace GameFlow
{
    public interface IFlowStep
    {
        string DisplayName { get; }
        UniTask Execute();
    }
}
