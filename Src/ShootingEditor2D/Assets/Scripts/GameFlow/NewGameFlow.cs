using Services;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootingEditor2D
{
    public class NewGameFlow
    {
        private string firstLevelName;
        private string agentName;
        private string agentDesc;

        public NewGameFlow(string firstLevelName, string agentName, string agentDesc)
        {
            this.firstLevelName = firstLevelName;
            this.agentName = agentName;
            this.agentDesc = agentDesc;
        }
        
        public void Start()
        {
            // 1.删除已有记忆
            AgentService.Instance.OnDeleteCurrentMemory += OnDeleteMemory;
            AgentService.Instance.SendMemoryDeleteCurrent();
        }
        
        private void OnDeleteMemory(bool success, string reason)
        {
            AgentService.Instance.OnDeleteCurrentMemory -= OnDeleteMemory;
            if (success)
            {
                Debug.Log("已删除记忆");
                // 2. 创建新Agent
                AgentService.Instance.OnCreateAgent += OnCreateAgent;
                AgentService.Instance.SendAgentCreate(this.agentName, this.agentDesc);
            }
            else
            {
                Debug.LogWarning($"创建Agent失败！原因: {reason}");
            }
        }

        
        private void OnCreateAgent(bool success, string reason)
        {
            AgentService.Instance.OnCreateAgent -= OnCreateAgent;

            if (success)
            {
                Debug.Log("已创建Agent");
                // 3. 备份记忆
                AgentService.Instance.OnBackupMemory += OnBackupMemory;
                AgentService.Instance.SendMemoryBackup(0);
            }
            else
            {
                Debug.LogWarning($"创建Agent失败！原因: {reason}");
            }
        }

        private void OnBackupMemory(bool success, string reason)
        {
            AgentService.Instance.OnBackupMemory -= OnBackupMemory;
            if (success)
            {
                Debug.Log("已备份记忆");
                // 4. 初始化保存数据
                SaveManager.Instance.Init(firstLevelName);
                // 5. 加载Agent
                AgentService.Instance.OnLoadAgent += OnLoadAgent;
                AgentService.Instance.SendAgentLoad();
            }
            else
            {
                Debug.LogWarning($"备份记忆失败！原因: {reason}");
            }
        }

        private void OnLoadAgent(bool success, List<string> agentNames)
        {
            AgentService.Instance.OnLoadAgent -= OnLoadAgent;
            if (success)
            {
                Debug.Log($"已加载的Agent: {string.Join(", ", agentNames)}");
                // 6. 启动Agent
                AgentService.Instance.OnStartScene += OnStartScene;
                AgentService.Instance.SendSceneStart(1);
            }
            else
            {
                Debug.LogWarning("加载Agent失败！");
            }
        }

        private void OnStartScene(bool success, string reason)
        {
            AgentService.Instance.OnStartScene -= OnStartScene;
            // 7. 启动场景
            if (success)
                SceneManager.LoadScene(firstLevelName);
        }
    }
}