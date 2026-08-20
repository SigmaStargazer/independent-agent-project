using Cysharp.Threading.Tasks;
using Services;
using System;
using System.Collections.Generic;

namespace Services
{
    public static class AgentServiceAsyncExtensions
    {
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