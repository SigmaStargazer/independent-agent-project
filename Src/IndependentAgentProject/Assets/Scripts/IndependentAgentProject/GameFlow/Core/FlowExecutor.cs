using Cysharp.Threading.Tasks;
using GameFlow;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FrameworkDesign;
using IndependentAgentProject;

namespace GameFlow
{
    public class FlowExecutor : MonoSingleton<FlowExecutor>
    {
        [SerializeField]
        private string titleSceneName = "Title";
        private bool isRunning = false;

        public async UniTask Execute(IGameFlow flow)
        {
            if (isRunning)
            {
                Debug.LogWarning("已有Flow正在执行");
                return;
            }
            isRunning = true;
            try
            {
                // 1. 显示加载界面
                if (flow.ShowLoadingScreen)
                {
                    await TransitionUI.Instance.FadeIn();
                }
                // 2. 执行步骤
                int total = flow.Steps.Count;
                for (int i = 0; i < total; i++)
                {
                    var step = flow.Steps[i];
                    if (flow.ShowLoadingScreen)
                    {
                        TransitionUI.Instance.SetProgress(
                            (float)i / total,
                            step.DisplayName);
                    }
                    await step.Execute();
                }
                // 3. 跳转至目标场景
                if (!string.IsNullOrEmpty(flow.TargetScene))
                {
                    await SceneManager.LoadSceneAsync(flow.TargetScene);
                }
                // 4. 隐藏加载界面
                if (flow.ShowLoadingScreen)
                {
                    TransitionUI.Instance.SetProgress(1f, UITextProvider.Get("flow_done"));
                    await UniTask.Delay(150);
                    await TransitionUI.Instance.FadeOut();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                // 1. 隐藏加载界面
                if (flow.ShowLoadingScreen)
                {
                    await TransitionUI.Instance.FadeOut();
                }
                // 2. 根据失败策略处理
                switch (flow.FailPolicy)
                {
                    case FlowFailPolicy.ReturnTitle:
                        await SceneManager.LoadSceneAsync(titleSceneName);
                        break;
                    case FlowFailPolicy.StayCurrentScene:
                        break;
                }
                // 3. 显示错误信息
                TransitionUI.Instance.ShowError(e.Message);
            }
            isRunning = false;
        }
    }
}
