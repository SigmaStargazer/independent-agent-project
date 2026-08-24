using Cysharp.Threading.Tasks;
using Services;
using System;
using System.Collections.Generic;

namespace Services
{
    public static class AgentServiceAsyncExtensions
    {
        /// <summary>v0.23.2：等待 Python 服务端连接就绪（端口文件就绪 + TCP 连接成功），超时抛异常。</summary>
        public static UniTask EnsureConnectedAsync()
        {
            return AgentService.Instance.EnsureConnectedAsync();
        }

        /// <summary>v0.23.0：发送 InitRequest，等待 Python 初始化记忆系统完成。</summary>
        public static UniTask InitAsync()
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnInit -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnInit += Handler;
            AgentService.Instance.SendInit();
            return tcs.Task;
        }

        /// <summary>v0.23.0b：发送 CloseRequest，等待 Python 关闭全部已初始化系统完成（回 Title）。</summary>
        public static UniTask CloseAsync()
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnClose -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnClose += Handler;
            AgentService.Instance.SendClose();
            return tcs.Task;
        }

        /// <summary>v0.23.1：发送 ApiTestRequest，测试当前面板 API 配置可用性（零系统，Title 阶段「测试后保存」触发）。</summary>
        public static UniTask ApiTestAsync(string category, string apiBase, string apiKey, string model)
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnApiTest -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnApiTest += Handler;
            AgentService.Instance.SendApiTest(category, apiBase, apiKey, model);
            return tcs.Task;
        }

        public static UniTask DeleteMemoryAsync()
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnDeleteCurrentMemory -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnDeleteCurrentMemory += Handler;
            AgentService.Instance.SendMemoryDeleteCurrent();
            return tcs.Task;
        }

        public static UniTask CreateAgentAsync(string name, string desc)
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnCreateAgent -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnCreateAgent += Handler;
            AgentService.Instance.SendAgentCreate(name, desc);
            return tcs.Task;
        }

        public static UniTask BackupMemoryAsync(int slotId)
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnBackupMemory -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnBackupMemory += Handler;
            AgentService.Instance.SendMemoryBackup(slotId);
            return tcs.Task;
        }

        public static UniTask RestoreMemoryAsync(int slotId)
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnRestoreMemory -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnRestoreMemory += Handler;
            AgentService.Instance.SendMemoryRestore(slotId);
            return tcs.Task;
        }

        public static UniTask<List<string>> LoadAgentAsync()
        {
            var tcs = new UniTaskCompletionSource<List<string>>();

            void Handler(bool success, List<string> agents)
            {
                AgentService.Instance.OnLoadAgent -= Handler;
                if (success)
                    tcs.TrySetResult(agents);
                else
                    tcs.TrySetException(new Exception("LoadAgent失败"));
            }
            AgentService.Instance.OnLoadAgent += Handler;
            AgentService.Instance.SendAgentLoad();
            return tcs.Task;
        }

        public static UniTask StartSceneAsync(int mapId)
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnStartScene -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnStartScene += Handler;
            AgentService.Instance.SendSceneStart(mapId);
            return tcs.Task;
        }

        public static UniTask StopSceneAsync()
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnStopScene -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnStopScene += Handler;
            AgentService.Instance.SendSceneStop();
            return tcs.Task;
        }

        public static UniTask InterruptAgentAsync(string stopReason = "系统关闭")
        {
            var tcs = new UniTaskCompletionSource();
            void Handler(bool success, string reason)
            {
                AgentService.Instance.OnInterruptAgent -= Handler;
                if (success)
                    tcs.TrySetResult();
                else
                    tcs.TrySetException(new Exception(reason));
            }
            AgentService.Instance.OnInterruptAgent += Handler;
            AgentService.Instance.SendAgentInterrupt(stopReason);
            return tcs.Task;
        }
    }
}