using Common;
//using Models;
using Network;
using SkillBridge.Message;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.PackageManager.Requests;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Services
{
    class AgentService : Singleton<AgentService>, IDisposable
    {
        // 事件。其他
        //public UnityEngine.Events.UnityAction<Result, string> OnCreateAgent;
        //public UnityEngine.Events.UnityAction<Result, string> OnStartScene;
        public UnityEngine.Events.UnityAction<bool, string> OnCreateAgent;
        public UnityEngine.Events.UnityAction<bool, List<string>> OnLoadAgent;
        public UnityEngine.Events.UnityAction<bool, string> OnStartScene;
        public UnityEngine.Events.UnityAction<string, string> OnGetAgentMessage;
        public UnityEngine.Events.UnityAction OnObserve;
        public UnityEngine.Events.UnityAction<bool, float> OnMoveAgent;
        public UnityEngine.Events.UnityAction OnInteract;
        NetMessage pendingMessage = null;
        bool connected = false;

        public AgentService()
        {
            AgentClient.Instance.OnConnect += OnGameServerConnect;
            AgentClient.Instance.OnDisconnect += OnGameServerDisconnect;
            MessageDistributer.Instance.Subscribe<AgentCreateResponse>(this.OnAgentCreate);// 记得写订阅消息和注销
            MessageDistributer.Instance.Subscribe<AgentLoadResponse>(this.OnAgentLoad);
            MessageDistributer.Instance.Subscribe<SceneStartResponse>(this.OnSceneStart);
            MessageDistributer.Instance.Subscribe<AgentSendMessageRequest>(this.OnAgentMessageGet);
            MessageDistributer.Instance.Subscribe<AgentObserveRequest>(this.OnAgentObserve);
            MessageDistributer.Instance.Subscribe<AgentMoveRequest>(this.OnAgentMove);
            MessageDistributer.Instance.Subscribe<AgentInteractRequest>(this.OnAgentInteract);
        }

        public void Dispose()
        {
            MessageDistributer.Instance.Unsubscribe<AgentCreateResponse>(this.OnAgentCreate);
            MessageDistributer.Instance.Unsubscribe<AgentLoadResponse>(this.OnAgentLoad);
            MessageDistributer.Instance.Unsubscribe<SceneStartResponse>(this.OnSceneStart);
            MessageDistributer.Instance.Unsubscribe<AgentSendMessageRequest>(this.OnAgentMessageGet);
            MessageDistributer.Instance.Unsubscribe<AgentObserveRequest>(this.OnAgentObserve);
            MessageDistributer.Instance.Unsubscribe<AgentMoveRequest>(this.OnAgentMove);
            MessageDistributer.Instance.Unsubscribe<AgentInteractRequest>(this.OnAgentInteract);
            AgentClient.Instance.OnConnect -= OnGameServerConnect;
            AgentClient.Instance.OnDisconnect -= OnGameServerDisconnect;
        }

        public void Init()
        {

        }

        public void ConnectToServer()
        {
            Debug.Log("ConnectToServer() Start ");
            //NetClient.Instance.CryptKey = this.SessionId;
            int port = GetPort();
            AgentClient.Instance.Init("127.0.0.1", GetPort());
            //AgentClient.Instance.Init("127.0.0.1", 8000);
            AgentClient.Instance.Connect();
        }

        int GetPort()
        {
            try
            {
                // 获取当前目录的上一级目录路径
                string projectRoot = Directory.GetParent(Application.dataPath).Parent?.FullName;
                // 只在 PC/Mac/Linux 构建的应用 中有效。Android/iOS：沙盒机制不允许访问外部路径
                Debug.Log($"projectRoot: {projectRoot}");

                if (projectRoot == null)
                {
                    throw new DirectoryNotFoundException("Unable to find the Src directory.");
                }

                string filePath = Path.Combine(projectRoot, "Data", "Config", "agent_server_port.txt");
                Debug.Log($"filePath: {filePath}");

                // 从文件中读取服务端端口号
                string portStr = File.ReadAllText(filePath).Trim();
                return int.Parse(portStr);
            }
            catch (FileNotFoundException)
            {
                Debug.Log("Server port file not found. Please ensure the server is running.");
                return 8000;
            }
        }


        void OnGameServerConnect(int result, string reason)
        {
            //Log.InfoFormat("LoadingMesager::OnGameServerConnect :{0} reason:{1}", result, reason);
            Debug.LogFormat("LoadingMesager::OnGameServerConnect :{0} reason:{1}", result, reason);
            if (AgentClient.Instance.Connected)
            {
                this.connected = true;
                if (this.pendingMessage != null)
                {
                    AgentClient.Instance.SendMessage(this.pendingMessage);
                    this.pendingMessage = null;
                }
            }
            else
            {
                if (!this.DisconnectNotify(result, reason))
                {
                    //MessageBox.Show(string.Format("网络错误，无法连接到服务器！\n RESULT:{0} ERROR:{1}", result, reason), "错误", MessageBoxType.Error);
                    Debug.LogFormat("网络错误，无法连接到服务器！\n RESULT:{0} ERROR:{1}", result, reason);
                }
            }
        }

        public void OnGameServerDisconnect(int result, string reason)
        {
            this.DisconnectNotify(result, reason);
            return;
        }

        bool DisconnectNotify(int result, string reason)
        {
            if (this.pendingMessage != null)
            {
                if (this.pendingMessage.Request.agentCreateRequest != null)
                {
                    if (this.OnCreateAgent != null)
                    {
                        this.OnCreateAgent(false, string.Format("服务器断开！\n RESULT:{0} ERROR:{1}", result, reason));
                    }
                }
                return true;
            }
            return false;
        }

        // 创建Agent
        // 发送消息
        public void SendAgentCreate(string name, string desc)
        {
            Debug.LogFormat("AgentCreateRequest::name :{0} desc:{1}", name, desc);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.agentCreateRequest = new AgentCreateRequest();
            message.Request.agentCreateRequest.Name = name;
            message.Request.agentCreateRequest.Desc = desc;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                this.pendingMessage = null;
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                this.pendingMessage = message;
                this.ConnectToServer();
            }
        }
        // 收到请求后
        void OnAgentCreate(object sender, AgentCreateResponse response)
        {
            Debug.LogFormat("OnAgentCreate:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnCreateAgent != null)
            {
                this.OnCreateAgent(response.Success, response.Errormsg);
            }
        }

        // 加载Agent
        // 发送消息
        public void SendAgentLoad()
        {
            Debug.LogFormat("AgentLoadRequest");
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.agentLoadRequest = new AgentLoadRequest();
            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                this.pendingMessage = null;
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                this.pendingMessage = message;
                this.ConnectToServer();
            }
        }

        // 收到请求后
        void OnAgentLoad(object sender, AgentLoadResponse response)
        {
            Debug.LogFormat("OnAgentLoad:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnLoadAgent != null)
            {
                this.OnLoadAgent(response.Success, response.AgentNames);
            }
        }

        // 启动场景
        public void SendSceneStart(int map_id)
        {
            Debug.LogFormat("SceneStarRequest::map_id :{0}", map_id);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.sceneStartRequest = new SceneStartRequest();
            message.Request.sceneStartRequest.MapId = map_id;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                this.pendingMessage = null;
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                this.pendingMessage = message;
                this.ConnectToServer();
            }
        }
        void OnSceneStart(object sender, SceneStartResponse response)
        {
            Debug.LogFormat("OnSceneStart:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnStartScene != null)
            {
                this.OnStartScene(response.Success, response.Errormsg);
            }
        }
        public void SendUserMessage(string agent, string user_message)
        {
            Debug.LogFormat("UserMessageRequest::[{0}]{1}", agent, user_message);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.userSendMessageRequest = new UserSendMessageRequest();
            message.Request.userSendMessageRequest.Agent = agent;
            message.Request.userSendMessageRequest.UserMessage = user_message;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                this.pendingMessage = null;
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                this.pendingMessage = message;
                this.ConnectToServer();
            }
        }
        void OnAgentMessageGet(object sender, AgentSendMessageRequest request)
        {
            Debug.LogFormat("OnAgentMessageGet:{0} [{1}]", request.Agent, request.AiMessage);
            if (this.OnGetAgentMessage != null)
            {
                this.OnGetAgentMessage(request.Agent, request.AiMessage);
            }
        }
        // 观察
        void OnAgentObserve(object sender, AgentObserveRequest request)
        {
            Debug.LogFormat("OnAgentMove");
            if (this.OnObserve != null)
            {
                this.OnObserve();
            }
        }
        // 移动
       void OnAgentMove(object sender, AgentMoveRequest request)
        {
            Debug.LogFormat("OnAgentMove:{0} [{1}]", request.IsRight, request.Distance);
            if (this.OnMoveAgent != null)
            {
                this.OnMoveAgent(request.IsRight, request.Distance);
            }
        }

        /// <summary>
        /// 交互
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        /// <exception cref="NotImplementedException"></exception>
        void OnAgentInteract(object sender, AgentInteractRequest request)
        {
            Debug.LogFormat("OnAgentInteract");
            if (this.OnInteract != null)
            {
                this.OnInteract();
            }
        }
    }
}
