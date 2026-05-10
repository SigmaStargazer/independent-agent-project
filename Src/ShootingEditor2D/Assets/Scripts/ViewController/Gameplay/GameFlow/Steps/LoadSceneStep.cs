using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ShootingEditor2D
{
    public class LoadSceneStep : IFlowStep
    {
        public string DisplayName => "º”‘ÿ≥°æ∞";

        private readonly string levalName;
        public LoadSceneStep(string levelName)
        {
            this.levalName = levelName;
        }

        public async UniTask Execute()
        {
            await SceneManager.LoadSceneAsync(levalName);
        }
    }
}