//WARNING: DON'T EDIT THIS FILE!!!
using Common;

namespace Network
{
    public class MessageDispatch<T> : Singleton<MessageDispatch<T>>
    {
        public void Dispatch(T sender, SkillBridge.Message.NetMessageResponse message)
        { 
            if (message.agentCreateResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentCreateResponse); }
            if (message.sceneStartResponse != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStartResponse); }
        }

        public void Dispatch(T sender, SkillBridge.Message.NetMessageRequest message)
        {
            if (message.agentCreateRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentCreateRequest); }
            if (message.sceneStartRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.sceneStartRequest); }
            if (message.userSendMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.userSendMessageRequest); }
            if (message.agentSendMessageRequest != null) { MessageDistributer<T>.Instance.RaiseEvent(sender, message.agentSendMessageRequest); }
        }
    }
}