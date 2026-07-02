using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    public class LoadSceneStep : IFlowStep
    {
        public string DisplayName => "加载场景";

        private readonly string levalName;
        public LoadSceneStep(string levelName)
        {
            this.levalName = levelName;
        }

        public async UniTask Execute()
        {
            await SceneManager.LoadSceneAsync(levalName);
            await UniTask.NextFrame();// 进入到场景的第一帧，使得各GameObject的Start()方法执行完毕
        }
    }
}