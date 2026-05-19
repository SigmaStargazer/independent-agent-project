//WARNING: DON'T EDIT THIS FILE!!!
using Common;

namespace Network
{
    public class MessageDispatch<T> : Singleton<MessageDispatch<T>>
    {
        public void Dispatch(T sender, SkillBridge.Message.NetMessageResponse message)
        { 
            if (message.agentCreateResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentCreateResponse); }
            if (message.agentLoadResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentLoadResponse); }
            if (message.sceneStartResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStartResponse); }
            if (message.sceneStopResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStopResponse); }
            if (message.agentInterruptResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentInterruptResponse);}
            if (message.memoryBackupResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.memoryBackupResponse); }
            if (message.memoryRestoreResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.memoryRestoreResponse);}
            if (message.memoryDeleteCurrentResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.memoryDeleteCurrentResponse);}
        }

        public void Dispatch(T sender, SkillBridge.Message.NetMessageRequest message)
        {
            if (message.agentCreateRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentCreateRequest); }
            if (message.agentLoadRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentLoadRequest); }
            if (message.sceneStartRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStartRequest); }
            if (message.sceneStopRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStopRequest); }
            if (message.agentInterruptRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentInterruptRequest);}
            if (message.userSendMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.userSendMessageRequest); }
            if (message.userSendMessageAllRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.userSendMessageAllRequest); }
            if (message.sendToolResultMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sendToolResultMessageRequest); }
            if (message.agentSendMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentSendMessageRequest); }
            if (message.agentObserveRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentObserveRequest); }
            if (message.agentMoveRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentMoveRequest); }
            if (message.agentInteractRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentInteractRequest); }
            if (message.agentSelectRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentSelectRequest); }
            if (message.agentInputRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentInputRequest); }
            if (message.agentPlanActionSequenceRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentPlanActionSequenceRequest); }
            if (message.agentStartActionSequenceRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentStartActionSequenceRequest); }
            if (message.agentContinueActionSequenceRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentContinueActionSequenceRequest); }
            if (message.agentStopActionSequenceRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentStopActionSequenceRequest); }
            if (message.memoryBackupRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.memoryBackupRequest); }
            if (message.memoryRestoreRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.memoryRestoreRequest); }
            if (message.memoryDeleteCurrentRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.memoryDeleteCurrentRequest); }
        }
    }
}