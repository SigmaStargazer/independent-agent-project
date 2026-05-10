using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameFlow;

namespace ShootingEditor2D
{
    public class ContinueGameFlow : IGameFlow
    {
        public bool ShowLoadingScreen => true;
        public FlowFailPolicy FailPolicy => FlowFailPolicy.ReturnTitle;
        public string TargetScene { get; }
        public IReadOnlyList<IFlowStep> Steps { get; }

        public ContinueGameFlow()
        {
            SaveData data = SaveManager.Instance.Load();
            this.TargetScene = data.LevelName;
            Steps = new List<IFlowStep>
            {
                new RestoreMemoryStep(0),

                new LoadAgentStep(),

                new StartSceneStep(this.TargetScene),
            };
        }
    }
    //public class ContinueGameFlow
    //{
    //    public ContinueGameFlow() { }

    //    public void Start()
    //    {
    //        // 1. 恢复记忆备份
    //        AgentService.Instance.OnRestoreMemory += OnRestoreMemory;
    //        AgentService.Instance.SendMemoryRestore(0);
    //    }
    //    private void OnRestoreMemory(bool success, string reason)
    //    {
    //        AgentService.Instance.OnRestoreMemory -= OnRestoreMemory;
    //        if (success)
    //        {
    //            Debug.Log("已恢复记忆备份！");
    //            // 2. 加载Agent
    //            AgentService.Instance.OnLoadAgent += OnLoadAgent;
    //            AgentService.Instance.SendAgentLoad();
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"恢复记忆备份失败！原因: {reason}");
    //        }
    //    }

    //    private void OnLoadAgent(bool success, List<string> agentNames)
    //    {
    //        AgentService.Instance.OnLoadAgent -= OnLoadAgent;
    //        if (success)
    //        {
    //            Debug.Log($"已加载的Agent: {string.Join(", ", agentNames)}");
    //            // 3. 启动Agent
    //            AgentService.Instance.OnStartScene += OnStartScene;
    //            AgentService.Instance.SendSceneStart(1); 
    //        }
    //        else
    //        {
    //            Debug.LogWarning("加载Agent失败！");
    //        }
    //    }

    //    private void OnStartScene(bool success, string reason)
    //    {
    //        AgentService.Instance.OnStartScene -= OnStartScene;
    //        if (success)
    //        {
    //            Debug.Log("Agent已启动！");
    //            // 4. 获取当前场景名，启动场景
    //            SaveData saveData = SaveManager.Instance.Load();
    //            var levelName = saveData.LevelName;
    //            SceneManager.LoadScene(levelName);
    //        }
    //        else
    //        {
    //            Debug.LogWarning($"Agent启动失败！原因: {reason}");
    //        }
    //    }
    //}
}
