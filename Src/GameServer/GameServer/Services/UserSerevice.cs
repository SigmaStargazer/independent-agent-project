using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Common;
using Network;
using SkillBridge.Message;
//using GameServer.Entities;
//using GameServer.Managers;

namespace GameServer.Services
{
    class UserService : Singleton<UserService>
    {

        public UserService()
        {
            MessageDistributer<NetConnection<NetSession>>.Instance.Subscribe<AgentCreateRequest>(this.OnAgentCreate);
            MessageDistributer<NetConnection<NetSession>>.Instance.Subscribe<SceneStartRequest>(this.OnSceneStart);
            MessageDistributer<NetConnection<NetSession>>.Instance.Subscribe<UserSendMessageRequest>(this.OnAgentMessageGet);
        }
        public void Init()
        {
            Log.InfoFormat("UserService Start.");
        }
        void OnAgentCreate(NetConnection<NetSession> sender, AgentCreateRequest request)
        {
            Log.InfoFormat("AgentCreateRequest: Name:{0}  Desc:{1}", request.Name, request.Desc);

            // 要发给客户端的消息
            NetMessage message = new NetMessage();
            message.Response = new NetMessageResponse();
            message.Response.agentCreateResponse = new AgentCreateResponse();
            message.Response.agentCreateResponse.Success = true;
            message.Response.agentCreateResponse.Errormsg = "";

            byte[] data = PackageHandler.PackMessage(message);
            sender.SendData(data, 0, data.Length);
        }
        void OnSceneStart(NetConnection<NetSession> sender, SceneStartRequest request)
        {
            Log.InfoFormat("SceneStartRequest: MapId:{0}", request.MapId);

            // 要发给客户端的消息
            NetMessage message = new NetMessage();
            message.Response = new NetMessageResponse();
            message.Response.sceneStartResponse = new SceneStartResponse();
            message.Response.sceneStartResponse.Success = true;
            message.Response.sceneStartResponse.Errormsg = "";

            byte[] data = PackageHandler.PackMessage(message);
            sender.SendData(data, 0, data.Length);
        }

        void OnAgentMessageGet(NetConnection<NetSession> sender, UserSendMessageRequest request)
        {
            Log.InfoFormat("UserSendMessageRequest: Agent:{0}  UserMessage:{1}", request.Agent, request.UserMessage);

            // 要发给客户端的消息
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.agentSendMessageRequest = new AgentSendMessageRequest();
            message.Request.agentSendMessageRequest.Agent = "小红";
            message.Request.agentSendMessageRequest.AiMessage = "闹钟设置好了！";

            byte[] data = PackageHandler.PackMessage(message);
            sender.SendData(data, 0, data.Length);
        }
    }
}
