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
using UnityEditor.MemoryProfiler;
using UnityEditor.PackageManager.Requests;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace Services
{
    class AgentService : Singleton<AgentService>, IDisposable
    {
        // 事件。其他
        //public UnityEngine.Events.UnityAction<Result, string> OnCreateAgent;
        //public UnityEngine.Events.UnityAction<Result, string> OnStartScene;
        public event UnityAction<bool, string> OnCreateAgent;
        public event UnityAction<bool, List<string>> OnLoadAgent;
        public event UnityAction<bool, string> OnStartScene;
        public event UnityAction<bool, string> OnStopScene;
        public event UnityAction<bool, string> OnInterruptAgent;

        public event UnityAction<bool, string> OnBackupMemory;
        public event UnityAction<bool, string> OnRestoreMemory;
        public event UnityAction<bool, string> OnDeleteCurrentMemory;

        public event UnityAction<string, string> OnGetAgentMessage;
        public event UnityAction<string, string, string> OnStopAction;

        public event UnityAction<string, string> OnObserve;
        public event UnityAction<string, string, int> OnMonitorTarget;
        public event UnityAction<string, string, int> OnGetMonitorRecords;
        public event UnityAction<string, string> OnGetWorldEventLog;

        public event UnityAction<string, bool, float> OnMoveAgent;
        public event Action<string, string, int, float, float> OnFollowTarget;

        public event UnityAction<string, string> OnInteract;
        public event UnityAction<string, int, string> OnSelect;
        public event UnityAction<string, string, string> OnInput;

        public event UnityAction<string, List<ActionStep>, string> OnPlanActionSequence;
        public event UnityAction<string, string> OnStartActionSequence;
        public event UnityAction<string, string> OnContinueActionSequence;
        public event UnityAction<string, string> OnStopActionSequence;

        public event Action<string, string, string, float, string, bool> OnSetTimer;
        public event UnityAction<string, string> OnGetTimerList;
        public event UnityAction<string, string, int> OnRemoveTimer;
        Queue<NetMessage> pendingMessages = new Queue<NetMessage>();
        bool connected = false; // 连接完成
        bool connecting = false; // 正在尝试连接

        public AgentService()
        {
            AgentClient.Instance.OnConnect += OnGameServerConnect;
            AgentClient.Instance.OnDisconnect += OnGameServerDisconnect;
            MessageDistributer.Instance.Subscribe<AgentCreateResponse>(this.OnAgentCreate);// 记得写订阅消息和注销
            MessageDistributer.Instance.Subscribe<AgentLoadResponse>(this.OnAgentLoad);
            MessageDistributer.Instance.Subscribe<SceneStartResponse>(this.OnSceneStart);
            MessageDistributer.Instance.Subscribe<SceneStopResponse>(this.OnSceneStop);
            MessageDistributer.Instance.Subscribe<AgentInterruptResponse>(this.OnAgentInterrupt);

            MessageDistributer.Instance.Subscribe<MemoryBackupResponse>(this.OnMemoryBackup);
            MessageDistributer.Instance.Subscribe<MemoryRestoreResponse>(this.OnMemoryRestore);
            MessageDistributer.Instance.Subscribe<MemoryDeleteCurrentResponse>(this.OnMemoryDeleteCurrent);

            MessageDistributer.Instance.Subscribe<AgentSendMessageRequest>(this.OnAgentMessageGet);
            MessageDistributer.Instance.Subscribe<AgentStopActionRequest>(this.OnAgentStopAction);

            MessageDistributer.Instance.Subscribe<AgentObserveRequest>(this.OnAgentObserve);
            MessageDistributer.Instance.Subscribe<AgentMonitorTargetRequest>(this.OnAgentMonitorTarget);
            MessageDistributer.Instance.Subscribe<AgentGetMonitorRecordsRequest>(this.OnAgentGetMonitorRecords);
            MessageDistributer.Instance.Subscribe<AgentGetWorldEventLogRequest>(this.OnAgentGetWorldEventLog);

            MessageDistributer.Instance.Subscribe<AgentMoveRequest>(this.OnAgentMove);
            MessageDistributer.Instance.Subscribe<AgentFollowTargetRequest>(this.OnAgentFollowTarget);

            MessageDistributer.Instance.Subscribe<AgentInteractRequest>(this.OnAgentInteract);
            MessageDistributer.Instance.Subscribe<AgentSelectRequest>(this.OnAgentSelect);
            MessageDistributer.Instance.Subscribe<AgentInputRequest>(this.OnAgentInput);

            MessageDistributer.Instance.Subscribe<AgentPlanActionSequenceRequest>(this.OnAgentPlanActionSequence);
            MessageDistributer.Instance.Subscribe<AgentStartActionSequenceRequest>(this.OnAgentStartActionSequence);
            MessageDistributer.Instance.Subscribe<AgentContinueActionSequenceRequest>(this.OnAgentContinueActionSequence);
            MessageDistributer.Instance.Subscribe<AgentStopActionSequenceRequest>(this.OnAgentStopActionSequence);

            MessageDistributer.Instance.Subscribe<AgentSetTimerRequest>(this.OnAgentSetTimer);
            MessageDistributer.Instance.Subscribe<AgentGetTimerListRequest>(this.OnAgentGetTimerList);
            MessageDistributer.Instance.Subscribe<AgentRemoveTimerRequest>(this.OnAgentRemoveTimer);
        }

        public void Dispose()
        {
            MessageDistributer.Instance.Unsubscribe<AgentCreateResponse>(this.OnAgentCreate);
            MessageDistributer.Instance.Unsubscribe<AgentLoadResponse>(this.OnAgentLoad);
            MessageDistributer.Instance.Unsubscribe<SceneStartResponse>(this.OnSceneStart);
            MessageDistributer.Instance.Unsubscribe<SceneStopResponse>(this.OnSceneStop);
            MessageDistributer.Instance.Unsubscribe<AgentInterruptResponse>(this.OnAgentInterrupt);

            MessageDistributer.Instance.Unsubscribe<MemoryBackupResponse>(this.OnMemoryBackup);
            MessageDistributer.Instance.Unsubscribe<MemoryRestoreResponse>(this.OnMemoryRestore);
            MessageDistributer.Instance.Unsubscribe<MemoryDeleteCurrentResponse>(this.OnMemoryDeleteCurrent);

            MessageDistributer.Instance.Unsubscribe<AgentSendMessageRequest>(this.OnAgentMessageGet);
            MessageDistributer.Instance.Unsubscribe<AgentStopActionRequest>(this.OnAgentStopAction);

            MessageDistributer.Instance.Unsubscribe<AgentObserveRequest>(this.OnAgentObserve);
            MessageDistributer.Instance.Unsubscribe<AgentMonitorTargetRequest>(this.OnAgentMonitorTarget);
            MessageDistributer.Instance.Unsubscribe<AgentGetMonitorRecordsRequest>(this.OnAgentGetMonitorRecords);
            MessageDistributer.Instance.Unsubscribe<AgentGetWorldEventLogRequest>(this.OnAgentGetWorldEventLog);

            MessageDistributer.Instance.Unsubscribe<AgentMoveRequest>(this.OnAgentMove);
            MessageDistributer.Instance.Unsubscribe<AgentFollowTargetRequest>(this.OnAgentFollowTarget);

            MessageDistributer.Instance.Unsubscribe<AgentInteractRequest>(this.OnAgentInteract);
            MessageDistributer.Instance.Unsubscribe<AgentSelectRequest>(this.OnAgentSelect);
            MessageDistributer.Instance.Unsubscribe<AgentInputRequest>(this.OnAgentInput);

            MessageDistributer.Instance.Unsubscribe<AgentPlanActionSequenceRequest>(this.OnAgentPlanActionSequence);
            MessageDistributer.Instance.Unsubscribe<AgentStartActionSequenceRequest>(this.OnAgentStartActionSequence);
            MessageDistributer.Instance.Unsubscribe<AgentContinueActionSequenceRequest>(this.OnAgentContinueActionSequence);
            MessageDistributer.Instance.Unsubscribe<AgentStopActionSequenceRequest>(this.OnAgentStopActionSequence);

            MessageDistributer.Instance.Unsubscribe<AgentSetTimerRequest>(this.OnAgentSetTimer);
            MessageDistributer.Instance.Unsubscribe<AgentGetTimerListRequest>(this.OnAgentGetTimerList);
            MessageDistributer.Instance.Unsubscribe<AgentRemoveTimerRequest>(this.OnAgentRemoveTimer);
            AgentClient.Instance.OnConnect -= OnGameServerConnect;
            AgentClient.Instance.OnDisconnect -= OnGameServerDisconnect;
        }

        public void Init()
        {

        }

        public void ConnectToServer()
        {
            if (this.connected || this.connecting)
                return;
            this.connecting = true;
            Debug.Log("ConnectToServer() Start ");
            //NetClient.Instance.CryptKey = this.SessionId;
            int port = GetPort();
            AgentClient.Instance.Init("127.0.0.1", port);
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
            this.connecting = false;
            //Log.InfoFormat("LoadingMesager::OnGameServerConnect :{0} reason:{1}", result, reason);
            Debug.LogFormat("LoadingMesager::OnGameServerConnect :{0} reason:{1}", result, reason);
            if (AgentClient.Instance.Connected)
            {
                this.connected = true;
                while (pendingMessages.Count > 0)
                {
                    AgentClient.Instance.SendMessage(pendingMessages.Dequeue());
                }
            }
            else
            {
                this.connected = false;
                if (!this.DisconnectNotify(result, reason))
                {
                    //MessageBox.Show(string.Format("网络错误，无法连接到服务器！\n RESULT:{0} ERROR:{1}", result, reason), "错误", MessageBoxType.Error);
                    Debug.LogFormat("网络错误，无法连接到服务器！\n RESULT:{0} ERROR:{1}", result, reason);
                }
            }
        }

        public void OnGameServerDisconnect(int result, string reason)
        {
            this.connecting = false;
            this.connected = false;
            this.DisconnectNotify(result, reason);
            return;
        }

        bool DisconnectNotify(int result, string reason)
        {
            if (pendingMessages.Count == 0)
            {
                return false;
            }

            string error =
                $"服务器断开！\n RESULT:{result} ERROR:{reason}";

            while (pendingMessages.Count > 0)
            {
                NetMessage message = pendingMessages.Dequeue();

                if (message.Request.agentCreateRequest != null)
                {
                    OnCreateAgent?.Invoke(false, error);
                }
                else if (message.Request.agentLoadRequest != null)
                {
                    OnLoadAgent?.Invoke(false, null);
                }
                else if (message.Request.sceneStartRequest != null)
                {
                    OnStartScene?.Invoke(false, error);
                }
                else if (message.Request.sceneStopRequest != null)
                {
                    OnStopScene?.Invoke(false, error);
                }
                else if (message.Request.memoryBackupRequest != null)
                {
                    OnBackupMemory?.Invoke(false, error);
                }
                else if (message.Request.memoryRestoreRequest != null)
                {
                    OnRestoreMemory?.Invoke(false, error);
                }
                else if (message.Request.memoryDeleteCurrentRequest != null)
                {
                    OnDeleteCurrentMemory?.Invoke(false, error);
                }
            }
            return true;
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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!this.connected && !this.connecting)
                {
                    this.ConnectToServer();
                }
            }
        }
        // 收到请求后
        void OnAgentCreate(object sender, AgentCreateResponse response)
        {
            Debug.LogFormat("OnAgentCreate::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnCreateAgent?.Invoke(response.Success, response.Errormsg);
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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        // 收到请求后
        void OnAgentLoad(object sender, AgentLoadResponse response)
        {
            Debug.LogFormat("OnAgentLoad::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnLoadAgent?.Invoke(response.Success, response.AgentNames);

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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }
        void OnSceneStart(object sender, SceneStartResponse response)
        {
            Debug.LogFormat("OnSceneStart::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnStartScene?.Invoke(response.Success, response.Errormsg);
        }

        // 停止场景
        public void SendSceneStop()
        {
            Debug.LogFormat("SceneStopRequest::");
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.sceneStopRequest = new SceneStopRequest();

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        void OnSceneStop(object sender, SceneStopResponse response)
        {
            Debug.LogFormat("OnSceneStop::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnStopScene?.Invoke(response.Success, response.Errormsg);
        }

        // 暂停Agent运行
        public void SendAgentInterrupt(string stopReason = "系统关闭")
        {
            Debug.LogFormat($"AgentInterruptRequest::Reason:{stopReason}");
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.agentInterruptRequest = new AgentInterruptRequest();
            message.Request.agentInterruptRequest.Reason = stopReason;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        void OnAgentInterrupt(object sender, AgentInterruptResponse response)
        {
            Debug.LogFormat("OnAgentInterrupt::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnInterruptAgent?.Invoke(response.Success, response.Errormsg);
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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        void OnMemoryBackup(object sender, MemoryBackupResponse response)
        {
            Debug.LogFormat("OnMemoryBackup::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnBackupMemory?.Invoke(response.Success, response.Errormsg);
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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        void OnMemoryRestore(object sender, MemoryRestoreResponse response)
        {
            Debug.LogFormat("OnMemoryRestore::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnRestoreMemory?.Invoke(response.Success, response.Errormsg);
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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        void OnMemoryDeleteCurrent(object sender, MemoryDeleteCurrentResponse response)
        {
            Debug.LogFormat("OnMemoryDeleteCurrent::Success:{0} [{1}]", response.Success, response.Errormsg);
            this.OnDeleteCurrentMemory?.Invoke(response.Success, response.Errormsg);
        }

        #endregion 记忆备份与恢复


        public void SendUserMessage(string agent, string userMessage, bool forceInterrupt = false)
        {
            Debug.LogFormat($"UserMessageRequest::agent:{agent} userMessage:{userMessage} forceInterrupt:{forceInterrupt}");
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.userSendMessageRequest = new UserSendMessageRequest();
            message.Request.userSendMessageRequest.Agent = agent;
            message.Request.userSendMessageRequest.UserMessage = userMessage;
            message.Request.userSendMessageRequest.ForceInterrupt = forceInterrupt;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }

        public void SendUserFeedback(string agent, string userFeedback, bool forceInterrupt = false)
        {
            Debug.LogFormat($"UserFeedbackRequest::agent:{agent} userFeedback:{userFeedback} forceInterrupt:{forceInterrupt}");
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.userSendFeedbackRequest = new UserSendFeedbackRequest();
            message.Request.userSendFeedbackRequest.Agent = agent;
            message.Request.userSendFeedbackRequest.UserFeedback = userFeedback;
            message.Request.userSendFeedbackRequest.ForceInterrupt = forceInterrupt;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
            }
        }

        public void SendUserMessageAll(string userMessage, bool forceInterrupt = false)
        {
            Debug.LogFormat("UserMessageAllRequest::userMessage:{0}", userMessage);
            NetMessage message = new NetMessage();
            message.Request = new NetMessageRequest();
            message.Request.userSendMessageAllRequest = new UserSendMessageAllRequest();
            message.Request.userSendMessageAllRequest.UserMessage = userMessage;
            message.Request.userSendMessageAllRequest.ForceInterrupt = forceInterrupt;

            // 判断连上没
            if (this.connected && AgentClient.Instance.Connected)
            {
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
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
                AgentClient.Instance.SendMessage(message);
            }
            else
            {
                pendingMessages.Enqueue(message);
                if (!AgentClient.Instance.Connected)
                {
                    this.ConnectToServer();
                }
            }
        }
        void OnAgentMessageGet(object sender, AgentSendMessageRequest request)
        {
            Debug.LogFormat("OnAgentMessageGet::Agent:{0} AiMessage:{1}", request.Agent, request.AiMessage);
            this.OnGetAgentMessage?.Invoke(request.Agent, request.AiMessage);
        }

        void OnAgentStopAction(object sender, AgentStopActionRequest response)
        {
            Debug.LogFormat($"OnAgentStopAction::Agent:{response.Agent} RequestId:{response.RequestId} ActionType:{response.ActionType}");
            this.OnStopAction?.Invoke(response.Agent, response.RequestId, response.ActionType);
        }
        // 观察
        void OnAgentObserve(object sender, AgentObserveRequest request)
        {
            Debug.LogFormat("OnAgentObserve::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            this.OnObserve?.Invoke(request.Agent, request.RequestId);
        }
        void OnAgentMonitorTarget(object sender, AgentMonitorTargetRequest request)
        {
            Debug.LogFormat($"OnAgentMonitorTarget::Agent:{request.Agent} RequestId:{request.RequestId} ObjectIndex:{request.ObjectIndex}");
            this.OnMonitorTarget?.Invoke(request.Agent, request.RequestId, request.ObjectIndex);
        }
        void OnAgentGetMonitorRecords(object sender, AgentGetMonitorRecordsRequest request)
        {
            Debug.LogFormat($"OnAgentGetMonitorRecords::Agent:{request.Agent} RequestId:{request.RequestId} MonitorIndex:{request.MonitorIndex}");
            this.OnGetMonitorRecords?.Invoke(request.Agent, request.RequestId, request.MonitorIndex);
        }
        void OnAgentGetWorldEventLog(object sender, AgentGetWorldEventLogRequest request)
        {
            Debug.LogFormat($"OnAgentGetWorldEventLog::Agent:{request.Agent} RequestId:{request.RequestId}");
            this.OnGetWorldEventLog?.Invoke(request.Agent, request.RequestId);
        }
        // 移动
       void OnAgentMove(object sender, AgentMoveRequest request)
        {
            Debug.LogFormat("OnAgentMove::Agent:{0} IsRight:{1} Distance:{2}", request.Agent, request.IsRight, request.Distance);
            this.OnMoveAgent?.Invoke(request.Agent, request.IsRight, request.Distance);
        }
        void OnAgentFollowTarget(object sender, AgentFollowTargetRequest request)
        {
            Debug.LogFormat($"OnAgentFollowTarget::Agent:{request.Agent} RequestId:{request.RequestId} ObjectIndex:{request.ObjectIndex} MinDistance:{request.MinDistance} MaxDistance:{request.MaxDistance}");
            this.OnFollowTarget?.Invoke(request.Agent, request.RequestId, request.ObjectIndex, request.MinDistance, request.MaxDistance);
        }

        #region 交互
        void OnAgentInteract(object sender, AgentInteractRequest request)
        {
            Debug.LogFormat("OnAgentInteract::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            this.OnInteract?.Invoke(request.Agent, request.RequestId);
        }

        void OnAgentSelect(object sender, AgentSelectRequest request)
        {
            Debug.LogFormat("OnAgentSelect::Agent:{0} Selection:{1} RequestId:{2}", request.Agent, request.Selection, request.RequestId);
            this.OnSelect?.Invoke(request.Agent, request.Selection, request.RequestId);
        }

        void OnAgentInput(object sender, AgentInputRequest request)
        {
            Debug.LogFormat("OnAgentInput::Agent:{0} Text:{1} RequestId:{2}", request.Agent, request.InputText, request.RequestId);
            this.OnInput?.Invoke(request.Agent, request.InputText, request.RequestId);
        }

        private void OnAgentPlanActionSequence(object sender, AgentPlanActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentPlanActionSequence::Agent:{0} ActionSequence:{1} RequestId:{2}", request.Agent, request.ActionSequences, request.RequestId);
            this.OnPlanActionSequence?.Invoke(request.Agent, request.ActionSequences, request.RequestId);
        }

        private void OnAgentStartActionSequence(object sender, AgentStartActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentStartActionSequence::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            this.OnStartActionSequence?.Invoke(request.Agent, request.RequestId);
        }

        private void OnAgentContinueActionSequence(object sender, AgentContinueActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentContinueActionSequence::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            this.OnContinueActionSequence?.Invoke(request.Agent, request.RequestId);
        }

        private void OnAgentStopActionSequence(object sender, AgentStopActionSequenceRequest request)
        {
            Debug.LogFormat("OnAgentStopActionSequence::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            this.OnStopActionSequence?.Invoke(request.Agent, request.RequestId);
        }

        void OnAgentSetTimer(object sender, AgentSetTimerRequest request)
        {
            Debug.LogFormat("OnAgentSetTimer::Agent:{0} RequestId:{1} TimerName:{2} DelaySeconds:{3} TimerDescription:{4} TimerRepeat:{5}",
                request.Agent, request.RequestId, request.TimerName, request.DelaySeconds, request.TimerDescription, request.TimerRepeat);
            this.OnSetTimer?.Invoke(request.Agent, request.RequestId, request.TimerName, request.DelaySeconds, request.TimerDescription, request.TimerRepeat);
        }

        void OnAgentGetTimerList(object sender, AgentGetTimerListRequest request)
        {
            Debug.LogFormat("OnAgentGetTimerList::Agent:{0} RequestId:{1}", request.Agent, request.RequestId);
            this.OnGetTimerList?.Invoke(request.Agent, request.RequestId);
        }

        void OnAgentRemoveTimer(object sender, AgentRemoveTimerRequest request)
        {
            Debug.LogFormat("OnAgentRemoveTimer::Agent:{0} RequestId:{1} TimerId:{2}", request.Agent, request.RequestId, request.TimerId);
            this.OnRemoveTimer?.Invoke(request.Agent, request.RequestId, request.TimerId);
        }

        #endregion
    }
}
