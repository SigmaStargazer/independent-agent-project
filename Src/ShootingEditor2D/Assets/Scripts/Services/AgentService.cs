using Common;
//using Models;
using Network;
using ProtoBuf;
using SkillBridge.Message;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        public UnityEngine.Events.UnityAction<bool, string> OnBackupMemory;
        public UnityEngine.Events.UnityAction<bool, string> OnRestoreMemory;
        public UnityEngine.Events.UnityAction<bool, string> OnDeleteCurrentMemory;
        public UnityEngine.Events.UnityAction<string, string> OnGetAgentMessage;
        public UnityEngine.Events.UnityAction<string, string> OnObserve;
        public UnityEngine.Events.UnityAction<string, bool, float> OnMoveAgent;
        public UnityEngine.Events.UnityAction<string, string> OnInteract;
        public UnityEngine.Events.UnityAction<string, int, string> OnSelect;
        public UnityEngine.Events.UnityAction<string, string, string> OnInput;
        public UnityEngine.Events.UnityAction<string, List<ActionStep>, string> OnPlanActionSequence;
        public UnityEngine.Events.UnityAction<string, string> OnStartActionSequence;
        public UnityEngine.Events.UnityAction<string, string> OnContinueActionSequence;
        public UnityEngine.Events.UnityAction<string, string> OnStopActionSequence;
        NetMessage pendingMessage = null;
        bool connected = false;

        public AgentService()
        {
            AgentClient.Instance.OnConnect += OnGameServerConnect;
            AgentClient.Instance.OnDisconnect += OnGameServerDisconnect;
            MessageDistributer.Instance.Subscribe<AgentCreateResponse>(this.OnAgentCreate);// 记得写订阅消息和注销
            MessageDistributer.Instance.Subscribe<AgentLoadResponse>(this.OnAgentLoad);
            MessageDistributer.Instance.Subscribe<SceneStartResponse>(this.OnSceneStart);
            MessageDistributer.Instance.Subscribe<MemoryBackupResponse>(this.OnMemoryBackup);
            MessageDistributer.Instance.Subscribe<MemoryRestoreResponse>(this.OnMemoryRestore);
            MessageDistributer.Instance.Subscribe<MemoryDeleteCurrentResponse>(this.OnMemoryDeleteCurrent);
            MessageDistributer.Instance.Subscribe<AgentSendMessageRequest>(this.OnAgentMessageGet);
            MessageDistributer.Instance.Subscribe<AgentObserveRequest>(this.OnAgentObserve);
            MessageDistributer.Instance.Subscribe<AgentMoveRequest>(this.OnAgentMove);
            MessageDistributer.Instance.Subscribe<AgentInteractRequest>(this.OnAgentInteract);
            MessageDistributer.Instance.Subscribe<AgentSelectRequest>(this.OnAgentSelect);
            MessageDistributer.Instance.Subscribe<AgentInputRequest>(this.OnAgentInput);
            MessageDistributer.Instance.Subscribe<AgentPlanActionSequenceRequest>(this.OnAgentPlanActionSequence);
            MessageDistributer.Instance.Subscribe<AgentStartActionSequenceRequest>(this.OnAgentStartActionSequence);
            MessageDistributer.Instance.Subscribe<AgentContinueActionSequenceRequest>(this.OnAgentContinueActionSequence);
            MessageDistributer.Instance.Subscribe<AgentStopActionSequenceRequest>(this.OnAgentStopActionSequence);
        }

        public void Dispose()
        {
            MessageDistributer.Instance.Unsubscribe<AgentCreateResponse>(this.OnAgentCreate);
            MessageDistributer.Instance.Unsubscribe<AgentLoadResponse>(this.OnAgentLoad);
            MessageDistributer.Instance.Unsubscribe<SceneStartResponse>(this.OnSceneStart);
            MessageDistributer.Instance.Unsubscribe<MemoryBackupResponse>(this.OnMemoryBackup);
            MessageDistributer.Instance.Unsubscribe<MemoryRestoreResponse>(this.OnMemoryRestore);
            MessageDistributer.Instance.Subscribe<MemoryDeleteCurrentResponse>(this.OnMemoryDeleteCurrent);
            MessageDistributer.Instance.Unsubscribe<AgentSendMessageRequest>(this.OnAgentMessageGet);
            MessageDistributer.Instance.Unsubscribe<AgentObserveRequest>(this.OnAgentObserve);
            MessageDistributer.Instance.Unsubscribe<AgentMoveRequest>(this.OnAgentMove);
            MessageDistributer.Instance.Unsubscribe<AgentInteractRequest>(this.OnAgentInteract);
            MessageDistributer.Instance.Unsubscribe<AgentSelectRequest>(this.OnAgentSelect);
            MessageDistributer.Instance.Unsubscribe<AgentInputRequest>(this.OnAgentInput);
            MessageDistributer.Instance.Unsubscribe<AgentPlanActionSequenceRequest>(this.OnAgentPlanActionSequence);
            MessageDistributer.Instance.Unsubscribe<AgentStartActionSequenceRequest>(this.OnAgentStartActionSequence);
            MessageDistributer.Instance.Unsubscribe<AgentContinueActionSequenceRequest>(this.OnAgentContinueActionSequence);
            MessageDistributer.Instance.Unsubscribe<AgentStopActionSequenceRequest>(this.OnAgentStopActionSequence);
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
            Debug.LogFormat("AgentCreateRequest::name:{0} desc:{1}", name, desc);
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
            Debug.LogFormat("OnAgentCreate::Success:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnCreateAgent != null)
            {
                this.OnCreateAgent(response.Success, response.Errormsg);
            }
        }

        // 加载Agent
        public void SendAgentLoad()
        {
            Debug.LogFormat("AgentLoadRequest::");
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
            Debug.LogFormat("OnAgentLoad::Success:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnLoadAgent != null)
            {
                this.OnLoadAgent(response.Success, response.AgentNames);
            }
        }

        // 启动场景
        public void SendSceneStart(int mapId)
        {
            Debug.LogFormat("SceneStarRequest::mapId:{0}", mapId);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.sceneStartRequest = new SceneStartRequest();
            message.Request.sceneStartRequest.MapId = mapId;

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
            Debug.LogFormat("OnSceneStart::Success:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnStartScene != null)
            {
                this.OnStartScene(response.Success, response.Errormsg);
            }
        }

        #region 记忆备份与恢复

        public void SendMemoryBackup(int slotId)
        { 
            Debug.LogFormat("MemoryBackupRequest::slotId:{0}", slotId);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.memoryBackupRequest = new MemoryBackupRequest();
            message.Request.memoryBackupRequest.SlotId = slotId;

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

        void OnMemoryBackup(object sender, MemoryBackupResponse response)
        {
            Debug.LogFormat("OnMemoryBackup::Success:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnBackupMemory != null)
            {
                this.OnBackupMemory(response.Success, response.Errormsg);
            }
        }


        public void SendMemoryRestore(int slotId)
        {
            Debug.LogFormat("MemoryRestoreRequest::slotId:{0}", slotId);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.memoryRestoreRequest = new MemoryRestoreRequest();
            message.Request.memoryRestoreRequest.SlotId = slotId;

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

        void OnMemoryRestore(object sender, MemoryRestoreResponse response)
        {
            Debug.LogFormat("OnMemoryRestore::Success:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnRestoreMemory != null)
            {
                this.OnRestoreMemory(response.Success, response.Errormsg);
            }
        }

        public void SendMemoryDeleteCurrent()
        {
            Debug.LogFormat("MemoryDeleteRequest::");
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.memoryDeleteCurrentRequest = new MemoryDeleteCurrentRequest();

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

        void OnMemoryDeleteCurrent(object sender, MemoryDeleteCurrentResponse response)
        {
            Debug.LogFormat("OnMemoryDeleteCurrent::Success:{0} [{1}]", response.Success, response.Errormsg);
            if (this.OnDeleteCurrentMemory != null)
            {
                this.OnDeleteCurrentMemory(response.Success, response.Errormsg);
            }
        }

        #endregion 记忆备份与恢复


        public void SendUserMessage(string agent, string userMessage)
        {
            Debug.LogFormat("UserMessageRequest::agent:{0} userMessage:{1}", agent, userMessage);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.userSendMessageRequest = new UserSendMessageRequest();
            message.Request.userSendMessageRequest.Agent = agent;
            message.Request.userSendMessageRequest.UserMessage = userMessage;

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
        public void SendToolResultMessage(string agent, string toolName, string requestId, string toolResultMessage)
        {
            Debug.LogFormat("ToolResultMessageRequest::agent:{0} toolName:{1} requestId:{2} toolResultMessage:{3}", agent, toolName, requestId, toolResultMessage);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.sendToolResultMessageRequest = new SendToolResultMessageRequest();
            message.Request.sendToolResultMessageRequest.Agent = agent;
            message.Request.sendToolResultMessageRequest.ToolName = toolName;
            message.Request.sendToolResultMessageRequest.RequestId = requestId;
            message.Request.sendToolResultMessageRequest.Result = toolResultMessage;

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
            Debug.LogFormat("OnAgentMessageGet::Agent:{0} AiMessage:{1}", request.Agent, request.AiMessage);
            if (this.OnGetAgentMessage != null)
            {
                this.OnGetAgentMessage(request.Agent, request.AiMessage);
            }
        }
        // 观察
        void OnAgentObserve(object sender, AgentObserveRequest request)
        {
            Debug.LogFormat("OnAgentObserve::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            if (this.OnObserve != null)
            {
                this.OnObserve(request.Agent, request.RequestId);
            }
        }
        // 移动
       void OnAgentMove(object sender, AgentMoveRequest request)
        {
            Debug.LogFormat("OnAgentMove::Agent:{0} IsRight:{1} Distance:{2}", request.Agent, request.IsRight, request.Distance);
            if (this.OnMoveAgent != null)
            {
                this.OnMoveAgent(request.Agent, request.IsRight, request.Distance);
            }
        }


        #region 交互
        void OnAgentInteract(object sender, AgentInteractRequest request)
        {
            Debug.LogFormat("OnAgentInteract::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            if (this.OnInteract != null)
            {
                this.OnInteract(request.Agent, request.RequestId);
            }
        }

        void OnAgentSelect(object sender, AgentSelectRequest request)
        {
            Debug.LogFormat("OnAgentSelect::Agent:{0} Selection:{1} RequestId:{2}", request.Agent, request.Selection, request.RequestId);
            if (this.OnSelect != null)
            {
                this.OnSelect(request.Agent, request.Selection, request.RequestId);
            }
        }

        void OnAgentInput(object sender, AgentInputRequest request)
        {
            Debug.LogFormat("OnAgentInput::Agent:{0} Text:{1} RequestId:{2}", request.Agent, request.InputText, request.RequestId);
            if (this.OnInput != null)
            {
                this.OnInput(request.Agent,request.InputText, request.RequestId);
            }
        }

        private void OnAgentPlanActionSequence(object sender, AgentPlanActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentPlanActionSequence::Agent:{0} ActionSequence:{1} RequestId:{2}", request.Agent, request.ActionSequences, request.RequestId);
            if (this.OnPlanActionSequence != null)
            {
                this.OnPlanActionSequence(request.Agent, request.ActionSequences, request.RequestId);
            }
        }

        private void OnAgentStartActionSequence(object sender, AgentStartActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentStartActionSequence::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            if (this.OnStartActionSequence != null)
            {
                this.OnStartActionSequence(request.Agent, request.RequestId);
            }
        }

        private void OnAgentContinueActionSequence(object sender, AgentContinueActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentContinueActionSequence::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            if (this.OnContinueActionSequence != null)
            {
                this.OnContinueActionSequence(request.Agent, request.RequestId);
            }
        }

        private void OnAgentStopActionSequence(object sender, AgentStopActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentStopActionSequence::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            if (this.OnStopActionSequence != null)
            {
                this.OnStopActionSequence(request.Agent, request.RequestId);
            }
        }

        #endregion
    }
}
