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
        }

        public void Dispatch(T sender, SkillBridge.Message.NetMessageRequest message)
        {
            if (message.agentCreateRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentCreateRequest); }
            if (message.agentLoadRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentLoadRequest); }
            if (message.sceneStartRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStartRequest); }
            if (message.userSendMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.userSendMessageRequest); }
            if (message.agentSendMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentSendMessageRequest); }
            if (message.agentObserveRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentObserveRequest); }
            if (message.agentMoveRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentMoveRequest); }
            if (message.agentInteractRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentInteractRequest); }
        }
    }
}