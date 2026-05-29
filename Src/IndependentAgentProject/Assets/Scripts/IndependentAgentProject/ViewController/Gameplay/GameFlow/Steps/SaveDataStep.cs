using Cysharp.Threading.Tasks;
using GameFlow;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class SaveDataStep : IFlowStep
    {
        public string DisplayName => "保存游戏数据";

        private readonly string levalName;

        public SaveDataStep(string levelName)
        {
            this.levalName = levelName;
        }

        public UniTask Execute()
        {
            SaveManager.Instance.Init(levalName);
            // 没有异步的步骤，直接返回即可UniTask.CompletedTask;
            return UniTask.CompletedTask;
        }
    }
}