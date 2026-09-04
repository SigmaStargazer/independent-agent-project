using Cysharp.Threading.Tasks;
using GameFlow;
using Services;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace IndependentAgentProject
{
    public class BackupMemoryStep : IFlowStep
    {
        public string DisplayName => UITextProvider.Get("step_backup_memory");

        private readonly int slotId;

        public BackupMemoryStep(int slotId)
        {
            this.slotId = slotId;
        }

        public async UniTask Execute()
        {
            await AgentServiceAsyncExtensions.BackupMemoryAsync(this.slotId);
        }
    }
}
