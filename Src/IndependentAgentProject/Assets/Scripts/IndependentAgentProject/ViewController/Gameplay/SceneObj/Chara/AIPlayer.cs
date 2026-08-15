using FrameworkDesign;
using IndependentAgentProject;
using Newtonsoft.Json;
using Services;
using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.VisualScripting;
using UnityEditor.U2D.Path.GUIFramework;
using UnityEngine;

namespace IndependentAgentProject
{
    public class AIPlayer : PlayerBase
    //public class Agent : ShootingEditor2DController
    {
        public override string Name => "小明";
        public override string Desc => "是一个帮助机器人";

        //private Trigger2DCheck mGroundCheck;

        // Action的上下文
        private ActionRuntime mCurActionRuntime;
        private List<ObserveRuntime> mObserveRuntimes = new();

        private const int MaxWorldEvents = 100;
        private readonly Queue<WorldEventRecord> mWorldEventLog = new();
        private readonly Dictionary<SceneObjBase, Action<SceneObjBase, string, string>> mWorldEventHandlers = new();
        private bool mWorldEventLogReady = false;

        private ActionSequenceRuntime mCurActionSequenceRuntime;
        private ActionSequenceRuntime mPlanningActionSequenceRuntime;
        private ConditionEvaluator mConditionEvaluator;
        private readonly List<TimerRuntime> mTimerRuntimes = new();
        private int mNextTimerId = 0;
        // 用于检查撞击场景对象时停止
        private HashSet<SceneObjBase> mTouchingObjs = new HashSet<SceneObjBase>();

        protected override void Awake()
        {
            base.Awake();
            //mGroundCheck = transform.Find("GroundCheck").GetComponent<Trigger2DCheck>();
            mConditionEvaluator = new ConditionEvaluator();

            this.RegisterEvent<GameOverEvent>(e =>
            {
                if (this.GetStateName() != "Dead")
                {
                    if (mCurActionSequenceRuntime != null)
                    {
                        mCurActionSequenceRuntime.State = ActionSequenceState.Aborted;
                    }
                }
            }).UnRegisterWhenGameObjectDestroyed(gameObject);
        }

        protected override void Update()
        {
            base.Update();
            // 判断是否有未完成的curActionCtx达到停止条件
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.Displacement = Mathf.Abs(transform.position.x - mCurActionRuntime.StartPostion.x);
                mCurActionRuntime.ActionTime += Time.deltaTime;
                // ========= 1. 错误终止优先 =========
                if (mCurActionRuntime.ErrorConditionFunc?.Invoke() == true)
                {
                    mCurActionRuntime.State = ActionState.Failed;
                    var finishedRuntime = mCurActionRuntime;
                    mCurActionRuntime = null;

                    ChangeState("Idle");
                    OnActionFinished(finishedRuntime);// 触发Hook
                    return;
                }
                // ========= 2. 正常完成 =========
                // 触发结束条件，并清空curActionCtx
                if (mCurActionRuntime.CompleteConditionFunc?.Invoke() == true)
                {
                    mCurActionRuntime.State = ActionState.Done;
                    var finishedRuntime = mCurActionRuntime;
                    mCurActionRuntime = null;

                    ChangeState("Idle");
                    OnActionFinished(finishedRuntime);// 触发Hook
                    return;
                }
            }

            this.UpdateTimers();

            mCurState?.OnUpdate(this);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (AgentManager.Instance != null)
                AgentManager.Instance.Register(this);
            SceneObjManager.OnSceneObjCreated += OnSceneObjCreated;

            if (SceneObjManager.Instance != null)
            {
                foreach (var sceneObj in SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject))
                {
                    OnSceneObjCreated(sceneObj);
                }
            }

            mWorldEventLogReady = false;
            StartCoroutine(EnableWorldEventLogNextFrame());
        }

        private IEnumerator EnableWorldEventLogNextFrame()
        {
            yield return null;
            mWorldEventLogReady = true;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (AgentManager.Instance != null)
                AgentManager.Instance.UnRegister(this);

            SceneObjManager.OnSceneObjCreated -= OnSceneObjCreated;
            mTimerRuntimes.Clear();
            ClearWorldEventLog();
        }

        private void OnSceneObjCreated(SceneObjBase obj)
        {
            mCurActionSequenceRuntime?.AddSceneObj(obj);
            mPlanningActionSequenceRuntime?.AddSceneObj(obj);
            RegisterWorldEventListener(obj);
        }

        public override void ChangeState(string stateName)
        {
            string oldState = GetStateName();
            base.ChangeState(stateName);
            if (!string.IsNullOrEmpty(oldState) && oldState != stateName)
                AppendWorldEventForSelf(oldState, stateName);
        }

        /// <summary>
        /// OnDisable时清空WorldEventLog
        /// </summary>
        private void ClearWorldEventLog()
        {
            foreach (var kv in this.mWorldEventHandlers.ToList())
                UnregisterWorldEventListener(kv.Key);
            this.mWorldEventHandlers.Clear();
            this.mWorldEventLog.Clear();
            this.mWorldEventLogReady = false;
        }

        /// <summary>
        /// 当场景新增场景对象时，注册监听WorldEvent
        /// </summary>
        /// <param name="obj"></param>
        private void RegisterWorldEventListener(SceneObjBase obj)
        {
            if (obj == null || obj.gameObject == this.gameObject || this.mWorldEventHandlers.ContainsKey(obj))
                return;

            Action<SceneObjBase, string, string> handler = (o, oldState, newState) =>
            {
                if (!mWorldEventLogReady)
                    return;

                AppendWorldEventForSceneObj(o, oldState, newState);

                if (newState == "Disappearance")
                    UnregisterWorldEventListener(o);
            };

            mWorldEventHandlers[obj] = handler;
            obj.OnStateChanged += handler;
            obj.OnObjectEnabled += handler;
            obj.OnObjectDisabled += handler;
        }

        private void UnregisterWorldEventListener(SceneObjBase obj)
        {
            if (obj == null || !mWorldEventHandlers.TryGetValue(obj, out var handler))
                return;

            obj.OnStateChanged -= handler;
            obj.OnObjectEnabled -= handler;
            obj.OnObjectDisabled -= handler;
            mWorldEventHandlers.Remove(obj);
        }

        /// <summary>
        /// 把场景中其他对象的状态变化，记录为世界事件
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="oldState"></param>
        /// <param name="newState"></param>
        private void AppendWorldEventForSceneObj(SceneObjBase obj, string oldState, string newState)
        {
            if (SceneObjManager.Instance == null)
                return;

            var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
            var renderer = new RuntimeInfoRenderer();
            string label = renderer.FormatSceneObjLabel(obj, sceneObjs);
            string msg = renderer.BuildSceneObjEventMsg(obj, oldState, newState, sceneObjs);

            AppendWorldEvent(label, oldState, newState, msg);
        }

        /// <summary>
        /// 把自身状态变化，记录为世界事件
        /// </summary>
        /// <param name="oldState"></param>
        /// <param name="newState"></param>
        private void AppendWorldEventForSelf(string oldState, string newState)
        {
            var renderer = new RuntimeInfoRenderer();
            string msg = renderer.BuildSelfEventMsg(Name, oldState, newState);

            AppendWorldEvent(Name, oldState, newState, msg);
        }

        private void AppendWorldEvent(string objectName, string oldState, string newState, string msg)
        {
            var record = new WorldEventRecord
            {
                Time = Time.time,
                ObjectName = objectName,
                OldState = oldState,
                NewState = newState,
                EventText = CreateMessageText(msg, includeObserveTagerts: false)
            };
            mWorldEventLog.Enqueue(record);
            while (mWorldEventLog.Count > MaxWorldEvents)
                mWorldEventLog.Dequeue();
        }

        public void GetWorldEventLog(string requestId)
        {
            var renderer = new RuntimeInfoRenderer();
            string text = renderer.RenderWorldEventLog(mWorldEventLog);

            AgentService.Instance.SendToolResultMessage(
                Name,
                "GetWorldEventLog",
                requestId,
                text
            );
        }

        public void GetWorldEventSummary(string requestId, int maxEvents, bool ignoreSelfEvents)
        {
            int limit = Mathf.Max(1, maxEvents);
            float now = Time.time;
            var filteredRecords = mWorldEventLog
                .Where(record => !(ignoreSelfEvents && record.ObjectName == Name))
                .ToList();
            var records = filteredRecords
                .Skip(Mathf.Max(0, filteredRecords.Count - limit))
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("[世界事件摘要]");
            sb.AppendLine($"最近事件数: {records.Count}");

            if (records.Count == 0)
            {
                sb.AppendLine("最近没有值得注意的世界事件。");
            }
            else
            {
                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    float elapsed = now - record.Time;
                    sb.AppendLine($"{i + 1}. {elapsed:F1}秒前，{record.ObjectName}: {record.OldState} -> {record.NewState}");
                }
            }

            AgentService.Instance.SendToolResultMessage(
                Name,
                "GetWorldEventSummary",
                requestId,
                sb.ToString()
            );
        }

        #region FSM Hook
        public override void OnDeadEnter()
        {
            base.OnDeadEnter();
            if (mCurActionSequenceRuntime != null)
            {
                mCurActionSequenceRuntime.State = ActionSequenceState.Aborted;
            }
        }
        #endregion

        /// <summary>
        /// OnActionFinished钩子逻辑：当Action结束且存在finishedCtx.Result.Message时，发送消息给llm
        /// </summary>
        /// <param name="finishedActionRuntime"></param>
        private void OnActionFinished(ActionRuntime finishedActionRuntime)
        {
            if (finishedActionRuntime == null)
            {
                Debug.LogError($"[{this.Name}OnActionFinished出错]finishedActionRuntime is null");
                return;
            }
            
            // 如果执行的是ActionSequence中的Action
            if (mCurActionSequenceRuntime?.State == ActionSequenceState.Executing)
            {
                // 1. 获取当前Action的EndEnv
                List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(mCurActionSequenceRuntime.SceneObjSnap);
                finishedActionRuntime.EndEnv = sceneObjsInfoDesc;

                // 2.执行Action结束逻辑
                if (finishedActionRuntime.State == ActionState.Done)
                    this.OnCurrentActionCompleted();
                else if (finishedActionRuntime.State == ActionState.Failed)
                {
                    // 暂停ActionSequence
                    mCurActionSequenceRuntime.State = ActionSequenceState.Aborted;
                    // 发送取消信息（追加动作序列回顾提示）
                    string result = finishedActionRuntime.Result.Message;
                    this.SendFeedbackToAgent($"[动作序列执行中断]{result}{ACTION_SEQUENCE_REVIEW_PROMPT}");

                }
                    return;
            }
            // 如果执行的是非ActionSequence中的Action
            else
            {
                // 1. 获取当前Action的EndEnv
                List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject));
                finishedActionRuntime.EndEnv = sceneObjsInfoDesc;

                if (finishedActionRuntime?.Result?.Message != null)
                {
                    this.SendFeedbackToAgent(finishedActionRuntime.Result.Message);
                }
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var sceneObj = collision.collider.GetComponent<SceneObjBase>();
            if (sceneObj == null) return;

            mTouchingObjs.Add(sceneObj);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            var sceneObj = collision.collider.GetComponent<SceneObjBase>();
            if (sceneObj == null) return;

            mTouchingObjs.Remove(sceneObj);

            // 如果当前有Action在执行, 就把离开的SceneObj从StartTouchingObjs中移除
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.StartTouchingObjs.Remove(sceneObj);
            }
        }

        //private void TurnBack(float horizontalDirection)
        //{
        //    if (horizontalDirection < 0 && transform.localScale.x > 0
        //        || horizontalDirection > 0 && transform.localScale.x < 0)
        //    {
        //        var localScale = transform.localScale;
        //        localScale.x = -localScale.x;
        //        transform.localScale = localScale;
        //    }
        //}

        // 获取自身状态信息
        private string GetSelfStateInfo(bool includeObserveTagerts = true)
        {
            Rigidbody2D rb = mRigidbody2D;
            Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;
            string speedDirX = velocity.x > 0.01f ? "right" : (velocity.x < -0.01f ? "left" : "");
            string speedDirY = velocity.y > 0.01f ? "up" : (velocity.y < -0.01f ? "down" : "");

            string speed_x_str = speedDirX == "" ? $"{Mathf.Abs(velocity.x)}m/s" : $"方向{speedDirX} {Mathf.Abs(velocity.x)}m/s";
            string speed_y_str = speedDirY == "" ? $"{Mathf.Abs(velocity.y)}m/s" : $"方向{speedDirY} {Mathf.Abs(velocity.y)}m/s";

            var actionInfoRenderer = new RuntimeInfoRenderer();
            var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);

            string ObserveTargetsInfo = includeObserveTagerts ?
                $"# 持续观察中的目标:\n{actionInfoRenderer.RenderObserveRuntimeSummary(this.mObserveRuntimes, sceneObjs)}\n"
                : "";

            string timerInfo = includeObserveTagerts ?
                $"# 进行中的定时器:\n{actionInfoRenderer.RenderTimerSummary(this.mTimerRuntimes)}\n"
                : "";

            // 拼接返回字符串
            string selfStateInfo = $"# 状态:{this.GetStateName()}\n" +
                $"# 横向速度:{speed_x_str}\n# 纵向速度:{speed_y_str}\n" +
                $"{ObserveTargetsInfo}" +
                $"{timerInfo}" +
                $"# 计划中的动作序列:\n{actionInfoRenderer.RenderActionSequenceRuntime(this.mPlanningActionSequenceRuntime, sceneObjs)}\n" +
                $"# 进行中的动作序列:\n{actionInfoRenderer.RenderActionSequenceRuntime(this.mCurActionSequenceRuntime, sceneObjs)}\n" +
                $"# 进行中的动作:\n{actionInfoRenderer.RenderActionRuntime(this.mCurActionRuntime, sceneObjs)}\n";

            return selfStateInfo;
        }
        /// <summary>
        /// 获取设备信息列表DevicesInfo，以及转化为的文字描述sceneObjsInfoDesc
        /// </summary>
        /// <returns></returns>
        /// 

        private string GetEnvSceneObjsInfo()
        { 
            var mapper = new SceneObjInfoMapper();
            var renderer = new SceneObjInfoRenderer();

            var (sceneObjsInfo, interactableObjInfo) = mapper.GetSceneObjsInfo(this.gameObject, SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject));
            var sceneObjsInfoDesc = renderer.Render(sceneObjsInfo, interactableObjInfo);
            return sceneObjsInfoDesc;
        }

        private string GetSceneObjSnapInfo(List<SceneObjBase> sceneObjSnap)
        {
            var mapper = new SceneObjInfoMapper();
            var renderer = new SceneObjInfoRenderer();

            var (sceneObjsInfo, interactableObjInfo) = mapper.GetSceneObjsInfo(this.gameObject, sceneObjSnap);
            var sceneObjsInfoDesc = renderer.Render(sceneObjsInfo, interactableObjInfo);
            return sceneObjsInfoDesc;
        }

        /// <summary>
        /// 获取地图描述
        /// </summary>
        /// <returns></returns>
        private string GetSceneInfo()
        {
            if (SceneInfo.Current == null)
                return "未知区域";

            List<string> lines = new();
            if (!string.IsNullOrWhiteSpace(SceneInfo.Current.DisplayName))
            {
                lines.Add($"场景名称: {SceneInfo.Current.DisplayName}");
            }
            if (!string.IsNullOrWhiteSpace(SceneInfo.Current.Description))
            {
                lines.Add($"场景描述: {SceneInfo.Current.Description}");
            }

            string result = string.Join("\n", lines);
            return string.IsNullOrWhiteSpace(result) ? "未知区域" : result;
        }

        /// <summary>
        /// 发送消息给Agent
        /// </summary>
        /// <param name="msg"></param>
        public void SendMessageToAgent(string msg, bool forceInterrupt = false, bool includeObserveTagerts = true)
        {
            string text = this.CreateMessageText(msg, includeObserveTagerts);
            // 发送给Agent
            AgentService.Instance.SendUserMessage(this.Name, text, forceInterrupt);
            // 测试用
            Debug.Log($"已发送消息给{this.Name}: {text}");
        }

        // 动作序列回顾提示文本（动作序列完成或中断时追加到反馈消息末尾，引导 Agent 复盘技能）
        private const string ACTION_SEQUENCE_REVIEW_PROMPT =
@"

<动作序列回顾>
你刚刚完成或中止了一次动作序列执行。请回顾这次经验：
- 如果这是一个新的行为模式，值得未来复用 → 调用 create_action_skill 总结为技能
- 如果已有类似技能但这次发现了新的使用场景 → 调用 add_action_skill_template 添加新模板
- 如果已有类似模板但这次发现了改进点 → 调用 refine_action_skill 精进
- 如果只是简单常规操作，不值得记住 → 无需操作

中止的序列也值得总结——分析失败原因并精进模板可能避免下次失败。
保存时请将具体参数替换为描述性占位符，以便未来复用。
</动作序列回顾>";

        public void SendFeedbackToAgent(string feedback, bool forceInterrupt = false, bool includeObserveTagerts = true)
        {
            string text = this.CreateMessageText(feedback, includeObserveTagerts);
            // 发送给Agent
            AgentService.Instance.SendUserFeedback(this.Name, text, forceInterrupt);
            // 测试用
            Debug.Log($"已发送反馈给{this.Name}: {text}");
        }

        private string CreateMessageText(string msg, bool includeObserveTagerts = true)
        {
            // 获取环境信息
            List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
            string selfStateInfo = this.GetSelfStateInfo(includeObserveTagerts);
            string sceneInfo = this.GetSceneInfo();
            string sceneObjsInfoDesc = this.GetEnvSceneObjsInfo();

            // 拼接
            List<string> messageToSend = new();
            messageToSend.Add(msg);
            messageToSend.Add($"<你的状态>\n{selfStateInfo}\n</你的状态>");
            messageToSend.Add($"<当前场景>\n{sceneInfo}\n</当前场景>");
            messageToSend.Add($"<环境>\n{sceneObjsInfoDesc}\n</环境>");

            string text = string.Join("\n\n", messageToSend);
            return text;
        }

        #region Agent动作指令。当AgentManager收到服务端LLM的指令时，会调用相应Agent示例的下列方法

        /// <summary>
        /// 移动入口守卫：若当前状态实现 IImmovableState（如 Hidden / Dead / Stunned），
        /// 直接通过工具结果反馈失败给 Python 端，并返回 true 让调用方提前 return。
        /// </summary>
        private bool RejectIfImmovable(string toolName, string requestId)
        {
            if (!IsImmovable) return false;
            string reason;
            if (IsDead)
                reason = "你已经死了，无法移动。";
            else
                reason = $"当前处于 {StateName} 状态，无法移动。";

            AgentService.Instance.SendToolResultMessage(
                Name,
                toolName,
                requestId,
                $"[{toolName}失败]{reason}"
            );
            return true;
        }

        public bool StopMovement(bool stopActionSequence = true)
        {
            bool success = false;
            if (mCurActionRuntime != null)
            {
                mCurActionRuntime.State = ActionState.Aborted;
                mCurActionRuntime = null;
                success = true;
            }
            if (stopActionSequence && mCurActionSequenceRuntime != null && mCurActionSequenceRuntime.State == ActionSequenceState.Executing)
            {
                mCurActionSequenceRuntime.State = ActionSequenceState.Aborted;
                success = true;
            }
            // 注意：若当前处于 IImmovableState（Hidden / Dead / Stunned 等），
            // 不强制切回 Idle，以避免破坏躲藏 / 死亡 / 击晕等语义。
            if (!IsImmovable)
                ChangeState("Idle");
            return success;
        }

        /// <summary>
        /// 受伤型重生反馈：被陷阱（IInvulnerableState 不命中时）传送回最近的 CheckPoint。
        /// v0.21.7-fix：从 ReturnToCheckPoint override 改到 ReturnToCheckPointByHurt override：
        /// - 中性版本 ReturnToCheckPoint 不再 override，调试 / 系统重置走 PlayerBase 默认（不发 feedback）；
        /// - 真正由「受伤」触发的传送才走这里，并仅在没被 IsInvulnerable 拦截时才发反馈。
        /// v0.21.7-fix_3：
        /// 1. 参数类型 SceneObjBase → string sourceName，让"伤害源"无须是 SceneObjBase（如 LaserTraining）；
        /// 2. 反馈消息使用相对方向 / 距离（口径与 SceneObjInfoRenderer 一致），不暴露绝对坐标；
        /// 3. 附带"触碰时面朝 / 触碰时横向速度"，便于 AI 复盘触碰瞬间姿态、并在同名伤害源（如多片"激光"）场景下做区分。
        ///
        /// 采样顺序（关键）：
        /// 1) 先采样 hitX / face / vx / vxDir（StopMovement 会清速度、base 调用会改 transform，所以必须最先采）；
        /// 2) StopMovement(true)；
        /// 3) IsInvulnerable 命中则 return（不发反馈、不传送）；
        /// 4) base.ReturnToCheckPointByHurt(sourceName) 完成实际传送；
        /// 5) 此时 transform.position 已是检查点 X → 算 dx = hitX - current.x，得到「最后位置」相对当前自身的方向 / 距离；
        /// 6) 拼多句反馈，给 Agent 发出。
        /// </summary>
        public override void ReturnToCheckPointByHurt(string sourceName = null)
        {
            float hitX = transform.position.x;
            string face = IsRight ? "right" : "left";
            float vx = mRigidbody2D != null ? mRigidbody2D.velocity.x : 0f;
            string vxDir = vx > 0.01f ? "right" : (vx < -0.01f ? "left" : "");

            StopMovement(stopActionSequence: true);

            if (IsInvulnerable) return;

            base.ReturnToCheckPointByHurt(sourceName);

            float dx = hitX - transform.position.x;
            string dirX = dx < 0 ? "left" : "right";
            float distX = Mathf.Abs(dx);

            string display = string.IsNullOrEmpty(sourceName) ? "陷阱" : sourceName;
            string vxPart = string.IsNullOrEmpty(vxDir)
                ? $"横向速度 {Mathf.Abs(vx):F2}m/s"
                : $"横向速度 {Mathf.Abs(vx):F2}m/s 方向{vxDir}";
            string text =
                $"[返回检查点]你触碰到: {display}。" +
                $"最后位置在你的 {dirX}方向 {distX:F2}m。" +
                $"触碰时面朝{face}，{vxPart}。" +
                $"已被传送回最近的检查点。当前动作/动作序列已中断。";
            this.SendFeedbackToAgent(text);
        }

        public void StopAction(string requestId, string actionType)
        {
            switch (actionType)
            {
                case "movement":
                    {
                        bool success = this.StopMovement();
                        AgentService.Instance.SendToolResultMessage(
                            Name,
                            "StopAction",
                            requestId,
                            success
                                ? "[停止动作结果]Movement已停止"
                                : "[停止动作结果]当前没有Movement动作"
                        );
                        break;
                    }

                case "observe":
                    {
                        int count = mObserveRuntimes.Count;
                        foreach (var runtime in mObserveRuntimes.ToList())
                        {
                            if (runtime.Target != null && runtime.StateChangedHandler != null)
                            {
                                runtime.Target.OnStateChanged -= runtime.StateChangedHandler;
                                runtime.Target.OnObjectEnabled -= runtime.StateChangedHandler;
                                runtime.Target.OnObjectDisabled -= runtime.StateChangedHandler;
                            }
                        }
                        mObserveRuntimes.Clear();
                        AgentService.Instance.SendToolResultMessage(
                            Name,
                            "StopAction",
                            requestId,
                            $"[停止动作结果]已停止{count}个观察任务"
                        );
                        break;
                    }
                default:
                    {
                        AgentService.Instance.SendToolResultMessage(
                            Name,
                            "StopAction",
                            requestId,
                            "[停止动作结果]actionType仅支持movement、observe"
                        );
                        break;
                    }
            }
        }

        /// <summary>
        /// Agent向用户发消息（RPC回调）
        /// </summary>
        public void OnGetAgentMessage(string requestId, string message)
        {
            AgentService.Instance.SendToolResultMessage(
                Name,
                "SendMessage",
                requestId,
                $"[消息发送结果]已向用户发送消息"
            );
        }

        /// <summary>
        /// 观察场景
        /// </summary>
        public void Observe(string requestId)
        {
            // 复用 CreateMessageText，自动补 <你的状态> / <当前场景> / <环境> 三块
            string messageToSend = this.CreateMessageText("[观察结果]");

            // 发送给Agent
            // tool_name = "observe"只用于日志打印，不用于判断
            AgentService.Instance.SendToolResultMessage(this.Name, "Observe", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        /// <summary>
        /// 持续观察目标
        /// </summary>
        /// <param name="agent"></param>
        /// <param name="requestId"></param>
        /// <param name="objectIndex"></param>
        /// <exception cref="NotImplementedException"></exception>
        private bool IsSceneObjectNameMatched(SceneObjBase target, string expectedName)
        {
            return string.Equals(target?.Name?.Trim(), expectedName?.Trim(), StringComparison.Ordinal);
        }

        public void MonitorTarget(string requestId, int objectIndex, string objectName)
        {
            // 1. 判断观察目标数量过多
            if (mObserveRuntimes.Count >= 3)
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "MonitorTarget",
                    requestId,
                    $"[持续观察失败] 最多同时持续观察3个目标！你并没有那么多的注意力去注意那么多目标！"
                );
                return;
            }
            // 2. 判断目标索引超出范围
            var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
            if (objectIndex < 0 || objectIndex >= sceneObjs.Count)
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "MonitorTarget",
                    requestId,
                    $"[持续观察失败] 索引[{objectIndex}]超出范围！"
                );
                return;
            }
            // 3. 校验目标名称是否匹配
            SceneObjBase target = sceneObjs[objectIndex];
            if (!IsSceneObjectNameMatched(target, objectName))
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "MonitorTarget",
                    requestId,
                    $"[持续观察失败] 目标校验失败：物体[{objectIndex}]当前是\"{target.Name}\"，不是你指定的\"{objectName}\"。请重新观察当前环境后再选择目标。"
                );
                return;
            }
            // 4. 判断目标是否已持续观察
            var existedIdx = mObserveRuntimes.FindIndex(r => r.Target == target);
            if (existedIdx >= 0)
            {
                int existedDisplayIdx = existedIdx + 1;
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "MonitorTarget",
                    requestId,
                    $"[持续观察结果] 你已经在持续观察 \"{target.Name}\"（持续观察目标[{existedDisplayIdx}]），无需重复挂上视线。要查看它的详细变化记录，调用 get_monitor_records 时填入持续观察目标序号 {existedDisplayIdx} 即可。"
                );
                return;
            }
            // 5， 创建持续观察任务
            var curTime = Time.time;
            var runtime = new ObserveRuntime
            {
                Target = target,
                TargetName = target.Name,
                ObserveStartTime = curTime,
                LastStateName = target.GetStateName(),
                State = ActionState.Doing,
                LastChangeTime = curTime
            };
            // 6. 回调函数注册
            runtime.StateChangedHandler = (obj, oldState, newState) =>
                {
                    // 0.更新状态变化次数
                    runtime.StateChangeNum++;
                    // 1.消息拼接
                    var curTime = Time.time;
                    var elapsed = curTime - runtime.LastChangeTime;
                    string elapsedKey = runtime.StateChangeNum == 1 ? $"距离开始观察" : $"距离上次状态改变";
                    var observeTime = curTime - runtime.ObserveStartTime;
                    string msg =
                        $"[第{runtime.StateChangeNum}次状态变化]\n" +
                        $"观察时长:{observeTime:F1}秒\n" +
                        $"状态变化:{oldState} -> {newState}\n" +
                        $"{elapsedKey}:{elapsed:F1}秒前";
                    // 2.记录
                    string record = this.CreateMessageText(msg: msg, includeObserveTagerts:false);
                    runtime.Records.Enqueue(record);
                    while (runtime.Records.Count > ObserveRuntime.MaxRecords)
                    {
                        runtime.Records.Dequeue();
                    }
                    runtime.UnreadCount++;
                    // 3.更新状态
                    runtime.LastStateName = newState;
                    runtime.LastChangeTime = curTime;

                    // 4. 目标消失时，结束该路观察并发 Feedback（含历史记录）
                    if (newState == "Disappearance")
                    {
                        HandleObserveTargetDisappeared(runtime, obj);
                    }
                };
            target.OnStateChanged += runtime.StateChangedHandler;
            target.OnObjectEnabled += runtime.StateChangedHandler;
            target.OnObjectDisabled += runtime.StateChangedHandler;
            // 7. 添加初始记录
            string initRecord = this.CreateMessageText($"[持续观察开始]\n" +
                $"目标:{target.Name}\n" +
                $"初始状态:{target.GetStateName()}",
                includeObserveTagerts:false);
            runtime.Records.Enqueue(initRecord);
            runtime.UnreadCount++;
            mObserveRuntimes.Add(runtime);
            // 8. 返回持续观察开始反馈（角色化文案 + 明确告知持续观察目标序号）
            int monitorTargetIndex = mObserveRuntimes.Count;
            AgentService.Instance.SendToolResultMessage(
                Name,
                "MonitorTarget",
                requestId,
                $"[持续观察结果] 你已经把视线挂在了 \"{target.Name}\" 身上。" +
                $"这是你目前的第 {monitorTargetIndex} 个持续观察目标（持续观察目标[{monitorTargetIndex}]）。" +
                $"今后想回顾它的详细变化记录时，调用 get_monitor_records 并填入持续观察目标序号 {monitorTargetIndex} 即可。"
            );
        }
        public void GetMonitorRecords(string requestId, int monitorTargetIndex)        {
            if (monitorTargetIndex < 1 || monitorTargetIndex > mObserveRuntimes.Count)
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "GetMonitorRecords",
                    requestId,
                    $"[获取观察记录失败] 持续观察目标[{monitorTargetIndex}] 不存在。请先在自我状态中查看「持续观察中的目标」列表，" +
                    $"再用列表里那个目标对应的持续观察目标序号重新调用。"
                );
                return;
            }
            // 1. 获取记录消息
            ObserveRuntime runtime = mObserveRuntimes[monitorTargetIndex - 1];
            var actionInfoRenderer = new RuntimeInfoRenderer();
            var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
            string text = actionInfoRenderer.RenderObserveTargetRuntime(runtime, sceneObjs);
            // 2. 发送消息
            AgentService.Instance.SendToolResultMessage(
                Name,
                "GetMonitorRecords",
                requestId,
                text
            );
            // 4. 重置未读记录数
            runtime.UnreadCount = 0;
        }

        /// <summary>
        /// 观察目标消失时的处理：输出历史观察记录、移除 ObserveRuntime、取消监听、发送 Feedback
        /// </summary>
        private void HandleObserveTargetDisappeared(ObserveRuntime runtime, SceneObjBase obj)
        {
            // 1. 取消事件监听
            if (runtime.Target != null && runtime.StateChangedHandler != null)
            {
                runtime.Target.OnStateChanged -= runtime.StateChangedHandler;
                runtime.Target.OnObjectEnabled -= runtime.StateChangedHandler;
                runtime.Target.OnObjectDisabled -= runtime.StateChangedHandler;
            }

            // 2. 获取消失前编号（OnObjectDisabled 在 UnRegister 之前触发，此时 sceneObjs 仍含该对象）
            var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
            int index = sceneObjs.IndexOf(obj);
            string targetLabel = index >= 0 ? $"{index}. {runtime.TargetName}" : runtime.TargetName;

            // 3. 渲染该目标迄今所有观察记录（含刚写入的 Disappearance Record）
            var renderer = new RuntimeInfoRenderer();
            string observeRecordsDetail = renderer.RenderObserveTargetRuntime(runtime, sceneObjs);

            // 4. 从 mObserveRuntimes 中移除
            mObserveRuntimes.Remove(runtime);

            // 5. 发送 Feedback（Feedback 本身即打断，无需额外 forceInterrupt 参数）
            string feedbackMsg =
                $"[持续观察中断]\n" +
                $"原因: 观察目标已从场景中消失\n" +
                $"对象: {targetLabel}\n" +
                $"说明: 该目标的持续观察任务已自动结束，注意力已释放\n\n" +
                $"==========观察记录汇总==========\n" +
                observeRecordsDetail;

            SendFeedbackToAgent(feedbackMsg, forceInterrupt: false, includeObserveTagerts: true);
        }

        /// <summary>
        /// 移动
        /// </summary>
        /// <param name="moveRight"></param>
        /// <param name="distance"></param>
        /// 
        public void Move(string requestId, bool moveRight, float distance)
        {
            if (RejectIfImmovable("Move", requestId)) return;
            this.StopMovement();
            this.moveRight = moveRight;
            float startX = transform.position.x;
            mCurActionRuntime = new ActionRuntime
            {
                ActionName = "Move",
                State = ActionState.Doing,
                Result = new ActionResult(),
                StartPostion = new Vector2(startX, transform.position.y),
                CompleteCondition = $"displacement >= {distance}",
                CompleteConditionFunc = () =>
                {
                    float dir = moveRight ? 1f : -1f;

                    bool arrived = mCurActionRuntime.Displacement >= distance;

                    if (arrived)
                    {
                        mCurActionRuntime.Result.Message = "[移动结果]到达目的地！";
                    }

                    return arrived;
                }
            };
            this.mCurActionRuntime.ErrorConditionFunc = () =>
            {
                // 碰撞判断
                foreach (var obj in this.mTouchingObjs)
                {
                    if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj))
                    {
                        if (this.mCurActionRuntime.Result == null)
                            this.mCurActionRuntime.Result = new ActionResult();

                        this.mCurActionRuntime.Result.Message =
                            $"[移动中断]撞击到物体: {obj?.Name ?? "Unknown SceneObj"}";

                        return true;
                    }
                }
                return false;
            };
            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            ChangeState("Move");

            string directionText = moveRight ? "右" : "左";
            AgentService.Instance.SendToolResultMessage(
                Name,
                "Move",
                requestId,
                $"[移动开始]方向:{directionText}，距离:{distance:F1}米。移动完成后你将收到通知。"
            );
        }

        public void FollowTarget(string requestId, int objectIndex, string objectName, float minDistance, float maxDistance)
        {
            if (RejectIfImmovable("FollowTarget", requestId)) return;
            var sceneObjs = SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject);
            if (objectIndex < 0 || objectIndex >= sceneObjs.Count)
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "FollowTarget",
                    requestId,
                    $"[跟随结果]失败！物体[{objectIndex}]不存在"
                );
                return;
            }

            SceneObjBase target = sceneObjs[objectIndex];
            if (!IsSceneObjectNameMatched(target, objectName))
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "FollowTarget",
                    requestId,
                    $"[跟随结果]失败！目标校验失败：物体[{objectIndex}]当前是\"{target.Name}\"，不是你指定的\"{objectName}\"。请重新观察当前环境后再选择目标。"
                );
                return;
            }

            this.StopMovement();
            TargetFollowing = target;
            FollowMinDistance = minDistance;
            FollowMaxDistance = maxDistance;
            mCurActionRuntime = new ActionRuntime
            {
                ActionName = "FollowTarget",
                State = ActionState.Doing,
                TargetFollowing = target,
                TargetName = target.Name,
                Result = new ActionResult()
            };
            this.mCurActionRuntime.ErrorConditionFunc = () =>
            {
                // 目标消失检测
                if (this.TargetFollowing == null || !this.TargetFollowing.gameObject.activeInHierarchy)
                {
                    if (this.mCurActionRuntime.Result == null)
                        this.mCurActionRuntime.Result = new ActionResult();

                    this.mCurActionRuntime.Result.Message =
                        $"[跟随中断]\n" +
                        $"原因: 跟随目标已从场景中消失\n" +
                        $"对象: {this.mCurActionRuntime.TargetName ?? "未知目标"}\n" +
                        $"说明: 跟随任务已结束";

                    return true;
                }

                // 碰撞判断
                foreach (var obj in this.mTouchingObjs)
                {
                    if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj))
                    {
                        if (this.mCurActionRuntime.Result == null)
                            this.mCurActionRuntime.Result = new ActionResult();

                        this.mCurActionRuntime.Result.Message =
                            $"[移动中断]撞击到物体: {obj?.Name ?? "Unknown SceneObj"}";

                        return true;
                    }
                }
                return false;
            };
            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            ChangeState("Follow");

            AgentService.Instance.SendToolResultMessage(
                Name,
                "FollowTarget",
                requestId,
                $"[跟随结果]开始跟随:{objectIndex}. {target.Name}"
            );
        }

        public override void OnFollowFixedUpdate()
        {
            if (TargetFollowing == null)
            {
                // 目标消失：走 Failed 路径
                if (mCurActionRuntime != null && mCurActionRuntime.State == ActionState.Doing)
                {
                    mCurActionRuntime.State = ActionState.Failed;
                    mCurActionRuntime.Result ??= new ActionResult();
                    mCurActionRuntime.Result.Message =
                        $"[跟随中断]\n" +
                        $"原因: 跟随目标已从场景中消失\n" +
                        $"对象: {mCurActionRuntime.TargetName ?? "未知目标"}\n" +
                        $"说明: 跟随任务已结束";

                    var finishedRuntime = mCurActionRuntime;
                    mCurActionRuntime = null;
                    TargetFollowing = null;
                    ChangeState("Idle");
                    OnActionFinished(finishedRuntime);
                }
                else
                {
                    TargetFollowing = null;
                    ChangeState("Idle");
                }
                return;
            }

            // v0.22.21: 转向与速度收敛到 CharaBase.OnFollowFixedUpdate（面向目标，不从 velocity.x 推导），
            // 子类只保留业务（目标消失处理）。三分支的 TurnBack 与速度赋值删除。
            base.OnFollowFixedUpdate();
        }

        /// <summary>
        /// 交互入口守卫：当前处于 Dead 状态时拒绝执行任何交互（Interact / Select / TextInput）。
        /// Hidden 等其它 IImmovableState 仍允许交互（PRD-3.5 / PRD-3.6）。
        /// </summary>
        private bool RejectIfDead(string toolName, string requestId)
        {
            if (!IsDead) return false;
            AgentService.Instance.SendToolResultMessage(
                Name,
                toolName,
                requestId,
                $"[{toolName}失败]你已经死了，无法执行交互。"
            );
            return true;
        }

        /// <summary>
        /// 交互
        /// </summary>
        public void DoInteract(string requestId)
        {
            if (RejectIfDead("Interact", requestId)) return;
            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("场景中未找到 SceneObjManager！");
                return;
            }
            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.Interact(this.gameObject);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
            string messageToSend = $"[交互结果]{result}";
            AgentService.Instance.SendToolResultMessage(this.Name, "Interact", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        public void DoSelect(int selection, string requestId)
        {
            if (RejectIfDead("Select", requestId)) return;
            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("场景中未找到 SceneObjManager！");
                return;
            }
            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.Select(this.gameObject, selection);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
            string messageToSend = $"[选择结果]{result}";
            AgentService.Instance.SendToolResultMessage(this.Name, "Select", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        public void DoTextInput(string inputText, string requestId)
        {
            if (RejectIfDead("TextInput", requestId)) return;
            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("场景中未找到 SceneObjManager！");
                return;
            }
            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.TextInput(this.gameObject, inputText);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
            string messageToSend = $"[输入结果]{result}";
            AgentService.Instance.SendToolResultMessage(this.Name, "TextInput", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        public void SetTimer(string requestId, string timerName, float delaySeconds, string timerDescription, bool timerRepeat)
        {
            if (delaySeconds <= 0f)
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "SetTimer",
                    requestId,
                    "[设置定时器失败] 延迟秒数必须大于0"
                );
                return;
            }

            if (string.IsNullOrWhiteSpace(timerName))
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "SetTimer",
                    requestId,
                    "[设置定时器失败] 定时器名称不能为空"
                );
                return;
            }

            var runtime = new TimerRuntime
            {
                TimerId = mNextTimerId++,
                TimerName = timerName,
                TimerDescription = string.IsNullOrWhiteSpace(timerDescription) ? "无描述" : timerDescription,
                DelaySeconds = delaySeconds,
                TimerRepeat = timerRepeat,
                StartTime = Time.time,
                TriggerTime = Time.time + delaySeconds
            };
            mTimerRuntimes.Add(runtime);

            string repeatText = timerRepeat ? "是" : "否";
            AgentService.Instance.SendToolResultMessage(
                Name,
                "SetTimer",
                requestId,
                $"[设置定时器结果]\n" +
                $"定时器id:{runtime.TimerId}\n" +
                $"名称:{runtime.TimerName}\n" +
                $"描述:{runtime.TimerDescription}\n" +
                $"将在{delaySeconds:F1}秒后触发\n" +
                $"重复:{repeatText}"
            );
        }

        public void GetTimerList(string requestId)
        {
            var actionInfoRenderer = new RuntimeInfoRenderer();
            AgentService.Instance.SendToolResultMessage(
                Name,
                "GetTimerList",
                requestId,
                actionInfoRenderer.RenderTimerListDetail(this.mTimerRuntimes)
            );
        }

        public void RemoveTimer(string requestId, int timerId)
        {
            int index = mTimerRuntimes.FindIndex(timer => timer.TimerId == timerId);
            if (index < 0)
            {
                AgentService.Instance.SendToolResultMessage(
                    Name,
                    "RemoveTimer",
                    requestId,
                    $"[删除定时器失败] 定时器id:{timerId} 不存在"
                );
                return;
            }

            var removedTimer = mTimerRuntimes[index];
            mTimerRuntimes.RemoveAt(index);
            AgentService.Instance.SendToolResultMessage(
                Name,
                "RemoveTimer",
                requestId,
                $"[删除定时器结果] 已删除:: 定时器id:{removedTimer.TimerId} 名称:{removedTimer.TimerName}"
            );
        }

        private void UpdateTimers()
        {
            if (mTimerRuntimes.Count == 0)
            {
                return;
            }

            float curTime = Time.time;
            for (int i = mTimerRuntimes.Count - 1; i >= 0; i--)
            {
                var timer = mTimerRuntimes[i];
                if (curTime < timer.TriggerTime)
                {
                    continue;
                }

                string repeatHint = timer.TimerRepeat ? "，将按相同间隔重复触发" : "";
                this.SendFeedbackToAgent(
                    $"[定时器到期]\n" +
                    $"定时器id:{timer.TimerId}\n" +
                    $"名称:{timer.TimerName}\n" +
                    $"描述:{timer.TimerDescription}{repeatHint}"
                );

                if (timer.TimerRepeat)
                {
                    timer.StartTime = curTime;
                    timer.TriggerTime = curTime + timer.DelaySeconds;
                }
                else
                {
                    mTimerRuntimes.RemoveAt(i);
                }
            }
        }

        #endregion

        #region ActionSequence相关
        public void PlanActionSequence(List<ActionStep> actionSequence, string requestId)
        {
            if (actionSequence == null || actionSequence.Count == 0)
            {
                // 这块尽量在服务端处理
                AgentService.Instance.SendToolResultMessage(
                    this.Name,
                    "PlanActionSequence", 
                    requestId, 
                    "[动作序列规划结果]ActionSequence为空！"
                    );
                return;
            }
            Debug.Log($"[{this.Name}] 收到动作序列规划请求，共 {actionSequence.Count} 个动作");

            try
            {
                // 1.创建mPlanningActionSequenceRuntime
                // 考虑是否要做暂停mCurActionSequenceRuntime
                // 释放旧的 planning runtime
                if (this.mPlanningActionSequenceRuntime != null)
                {
                    this.mPlanningActionSequenceRuntime.Dispose();
                    this.mPlanningActionSequenceRuntime = null;
                }
                this.mPlanningActionSequenceRuntime = new ActionSequenceRuntime(actionSequence, SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject));

                // 2. 生成 ConditionContext 快照
                List<SceneObjBase> sceneObjs = new List<SceneObjBase>();
                sceneObjs.AddRange(mPlanningActionSequenceRuntime.SceneObjSnap);// 以后再追加其他chara
                var conditionCxt = new ConditionContext(this, sceneObjs);
                // actionTime / displacement 在Plan阶段都为0
                conditionCxt.ActionTime = 0f;
                conditionCxt.Displacement = 0f;

                // 3. 校验动作序列
                List<ConditionEvalResult> validateResults = this.mConditionEvaluator.ValidateAll(actionSequence, conditionCxt);
                var errorMessages = new List<string>();

                foreach (var r in validateResults)
                {
                    if (r.Status == ConditionEvalStatus.Error && !string.IsNullOrEmpty(r.ErrorMessage))
                    {
                        errorMessages.Add(r.ErrorMessage);
                    }
                }

                if (errorMessages.Count > 0)
                {
                    string combinedError = string.Join("\n", errorMessages);
                    AgentService.Instance.SendToolResultMessage(
                         this.Name,
                         "PlanActionSequence",
                         requestId,
                         $"[动作序列规划结果]动作序列校验未通过:\n{combinedError}"
                         );
                    return;
                }
                else // 4.生成确认信息
                {
                    string sceneObjsInfoDesc = "";

                    this.mPlanningActionSequenceRuntime.CreateActionRuntimeLog(this);
                    var sceneObjsSnap = this.mPlanningActionSequenceRuntime.SceneObjSnap;
                    var mapper = new SceneObjInfoMapper();
                    var renderer = new SceneObjInfoRenderer();

                    var (sceneObjsInfo, interactableObjInfo) = mapper.GetSceneObjsInfo(this.gameObject, sceneObjsSnap);
                    for (int i = 0; i < sceneObjsInfo.Count; i++)
                    {
                        string sceneObjInfoDesc = $"\n{i}. {renderer.RenderSceneObj(sceneObjsInfo[i])}";
                        sceneObjsInfoDesc += sceneObjInfoDesc;
                    }

                    string messageToSend = $"[动作序列规划结果]计划中的动作序列已准备就绪！" +
                        $"建议在开始动作序列前，先对各动作的结束条件中的object[i]是否能和下列物体快照中的物体能对应上进行核对（如核对不上，可再次使用ActionSequence规划功能进行修改）：" +
                        $"{sceneObjsInfoDesc}";
                    AgentService.Instance.SendToolResultMessage(
                         this.Name,
                         "PlanActionSequence",
                         requestId,
                         messageToSend
                         );
                }
            }
            catch (Exception e)
            {
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "PlanActionSequence",
                     requestId,
                     $"[动作序列规划结果]出错：{e.Message}"
                     );
            }

        }

        public void StartActionSequence(string requestId)
        {
            if (mPlanningActionSequenceRuntime == null)
            {
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "StartActionSequence",
                     requestId,
                     $"[动作序列确认开始执行结果]失败: 没有计划中的动作序列"
                     );
                return;
            }

            try
            {
                // 1.停止当前Action
                this.StopMovement(false);
                // v0.21.7_fix_1 F1: 移除此处裸 ChangeState("Idle")，
                // StopMovement(false) 内部已有 IImmovableState 守卫，避免覆盖。
                // 2.替换ActionSequence
                // 保存旧的 runtime
                var oldRuntime = this.mCurActionSequenceRuntime;
                // 用 planning runtime 替换当前 runtime
                this.mCurActionSequenceRuntime = this.mPlanningActionSequenceRuntime;
                this.mPlanningActionSequenceRuntime = null;
                // 释放旧的 runtime
                if (oldRuntime != null)
                {
                    oldRuntime.Dispose();   // 清理内部引用、日志、事件等
                    oldRuntime = null;      // 断开本地引用
                }
                // 3.启动ActionSequence
                mCurActionSequenceRuntime.State = ActionSequenceState.Executing;
                this.ExecuteCurAction();
                // 4.发送消息
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "StartActionSequence",
                     requestId,
                     $"[动作序列确认开始执行结果]成功: 共计{this.mCurActionSequenceRuntime.ActionSequence.Count}个动作"
                     );
            }
            catch (Exception e)
            {
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "StartActionSequence",
                     requestId,
                     $"[动作序列确认开始执行结果]失败: {e.Message}"
                     );
                return;
            }
        }

        public void ContinueActionSequence(string requestId)
        {
            if (mCurActionSequenceRuntime == null)
            {
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "ContinueActionSequence",
                     requestId,
                     $"[动作序列继续执行结果]失败: 没有执行中的动作序列"
                     );
                return;
            }
            if (mCurActionSequenceRuntime.State == ActionSequenceState.Executing)
            { 
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "ContinueActionSequence",
                     requestId,
                     $"[动作序列继续执行结果]失败: 动作序列已执行，无需再次启动"
                     );
                return;
            }

            // 1.启动ActionSequence
            mCurActionSequenceRuntime.State = ActionSequenceState.Executing;
            this.ExecuteCurAction();
            // 2.发送消息
            AgentService.Instance.SendToolResultMessage(
                 this.Name,
                 "ContinueActionSequence",
                 requestId,
                 $"[动作序列继续执行结果]成功: 共计{this.mCurActionSequenceRuntime.ActionSequence.Count}个动作，当前正在执行第{this.mCurActionSequenceRuntime.CurActionIndex+1}个动作"
                 );
        }

        public void StopActionSequence(string requestId)
        {
            if (mCurActionSequenceRuntime == null)
            {
                AgentService.Instance.SendToolResultMessage(
                     this.Name,
                     "StopActionSequence",
                     requestId,
                     $"[动作序列停止结果]失败: 没有执行中的动作序列"
                     );
                return;
            }

            // 1. 停止当前的Action
            this.StopMovement(false);
            // v0.21.7_fix_1 F2: 移除此处裸 ChangeState("Idle")，
            // 用户主动停 AS 不应解除 Hidden/Dead/Stunned 等 IImmovableState 保护语义。
            // 2. 设置终止状态（不清除是为了还能调用log）
            mCurActionSequenceRuntime.State = ActionSequenceState.Aborted;
            // 3. 发送取消信息
            AgentService.Instance.SendToolResultMessage(
                 this.Name,
                 "StopActionSequence",
                 requestId,
                 $"[动作序列停止结果]停止成功"
                 );
        }

        private void ExecuteCurAction()
        {
            if (mCurActionSequenceRuntime == null)
            {
                return;
            }

            // AS执行完成
            ActionStep curAction = mCurActionSequenceRuntime.GetCurActionStep();
            if (curAction != null)
            {
                if (curAction.Move != null)
                {
                    this.ExecuteMoveAction(mCurActionSequenceRuntime);
                }
                else if (curAction.Wait != null)
                {
                    this.ExecuteWaitAction(mCurActionSequenceRuntime);
                }
                else if (curAction.Interact != null)
                {
                    this.ExecuteInteractAction(mCurActionSequenceRuntime);
                }
                else if (curAction.Select != null)
                {
                    this.ExecuteSelectAction(mCurActionSequenceRuntime);
                }
                else if (curAction.Input != null)
                {
                    this.ExecuteInputAction(mCurActionSequenceRuntime);
                }
                else
                {
                    Debug.Log($"[{this.Name}]未定义的ActionStep");
                }
            }
            else
            {
                // AS执行完成的逻辑
                this.CompleteActionSequence();
            }
        }


        private void ExecuteMoveAction(ActionSequenceRuntime actionSequenceRuntime)
        {
            // Hidden / Dead / Stunned 等 IImmovableState 下禁止 ActionSequence 的位移行为
            if (IsImmovable)
            {
                var curAction0 = actionSequenceRuntime.GetCurActionStep();
                var curRuntime0 = actionSequenceRuntime.GetCurActionRuntime();
                curRuntime0.Result ??= new ActionResult();
                curRuntime0.Result.Message = IsDead
                    ? "[动作中断]你已经死了，无法移动。"
                    : $"[动作中断]当前处于 {StateName} 状态，无法移动。";
                curRuntime0.State = ActionState.Failed;

                var finishedRuntime0 = curRuntime0;
                this.mCurActionRuntime = null;
                OnActionFinished(finishedRuntime0);
                return;
            }
            var curAction = actionSequenceRuntime.GetCurActionStep();
            this.moveRight = curAction.Move.direction == MoveAction.Direction.Right;
            this.mCurActionRuntime = actionSequenceRuntime.GetCurActionRuntime();

            if ( this.mCurActionRuntime.State == ActionState.Todo)
            {
                // 获取设备信息
                //List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(actionSequenceRuntime.SceneObjSnap);

                // 创建Condition判断上下文
                List<SceneObjBase> sceneObjs = new List<SceneObjBase>();
                sceneObjs.AddRange(actionSequenceRuntime.SceneObjSnap);// 以后再追加其他chara
                var conditionCxt = new ConditionContext(this, sceneObjs);

                // 更新curActionRuntime开始时的信息
                this.mCurActionRuntime.StartPostion = new Vector2(transform.position.x, transform.position.y);
                this.mCurActionRuntime.StartEnv = sceneObjsInfoDesc;

                // 移动时允许接触的物体
                var allowedIds = curAction.Move.AllowedContactObjIds;
                if (allowedIds != null)
                {
                    foreach (var id in allowedIds)
                    {
                        this.mCurActionRuntime.AllowedContactObjs
                            .Add(actionSequenceRuntime.SceneObjSnap[id]);
                    }
                }

                this.mCurActionRuntime.CompleteConditionFunc = () =>
                {
                    // 每帧更新动态变量
                    conditionCxt.ActionTime = this.mCurActionRuntime.ActionTime;
                    conditionCxt.Displacement = this.mCurActionRuntime.Displacement;
                    int index = mCurActionSequenceRuntime.CurActionIndex;

                    ConditionEvalResult result = this.mConditionEvaluator.Evaluate(index, curAction, conditionCxt);
                    // 当条件为True/Error时，停止Action
                    return result.Status != ConditionEvalStatus.False;
                };
                this.mCurActionRuntime.ErrorConditionFunc = () =>
                {
                    // 碰撞判断
                    foreach (var obj in this.mTouchingObjs)
                    {
                        if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj) && !this.mCurActionRuntime.AllowedContactObjs.Contains(obj))
                        {
                            if (this.mCurActionRuntime.Result == null)
                                this.mCurActionRuntime.Result = new ActionResult();

                            this.mCurActionRuntime.Result.Message =
                                $"撞击到物体: {obj?.Name ?? "Unknown SceneObj"}";

                            return true;
                        }
                    }
                    return false;
                };
            }
            this.mCurActionRuntime.State = ActionState.Doing;

            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            ChangeState("Move");
        }
        private void ExecuteWaitAction(ActionSequenceRuntime actionSequenceRuntime)
        {
            var curAction = actionSequenceRuntime.GetCurActionStep();
            this.mCurActionRuntime = actionSequenceRuntime.GetCurActionRuntime();
            if ( this.mCurActionRuntime.State == ActionState.Todo)
            {
                // 获取设备信息
                //List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(actionSequenceRuntime.SceneObjSnap);

                // 创建Condition判断上下文
                List<SceneObjBase> sceneObjs = new List<SceneObjBase>();
                sceneObjs.AddRange(actionSequenceRuntime.SceneObjSnap);// 以后再追加其他chara
                var conditionCxt = new ConditionContext(this, sceneObjs);

                // 更新curActionRuntime开始时的信息
                this.mCurActionRuntime.StartPostion = new Vector2(transform.position.x, transform.position.y);
                this.mCurActionRuntime.StartEnv = sceneObjsInfoDesc;

                // 等待时允许接触的物体（与 ExecuteMoveAction 对齐）
                var allowedIds = curAction.Wait.AllowedContactObjIds;
                if (allowedIds != null)
                {
                    foreach (var id in allowedIds)
                    {
                        this.mCurActionRuntime.AllowedContactObjs
                            .Add(actionSequenceRuntime.SceneObjSnap[id]);
                    }
                }

                this.mCurActionRuntime.CompleteConditionFunc = () =>
                {
                    // 每帧更新动态变量
                    conditionCxt.ActionTime = this.mCurActionRuntime.ActionTime;
                    conditionCxt.Displacement = this.mCurActionRuntime.Displacement;
                    int index = mCurActionSequenceRuntime.CurActionIndex;

                    ConditionEvalResult result = this.mConditionEvaluator.Evaluate(index, curAction, conditionCxt);
                    // 当条件为True/Error时，停止Action
                    return result.Status != ConditionEvalStatus.False;
                };
                this.mCurActionRuntime.ErrorConditionFunc = () =>
                {
                    // 碰撞判断
                    foreach (var obj in this.mTouchingObjs)
                    {
                        if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj)
                            && !this.mCurActionRuntime.AllowedContactObjs.Contains(obj))
                        {
                            if (this.mCurActionRuntime.Result == null)
                                this.mCurActionRuntime.Result = new ActionResult();

                            this.mCurActionRuntime.Result.Message =
                                $"撞击到物体: {obj?.Name ?? "Unknown SceneObj"}";

                            return true;
                        }
                    }
                    return false;
                };
            }
            this.mCurActionRuntime.State = ActionState.Doing;

            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            // v0.21.7_fix_1 F3: Hidden 等 IImmovableState 下不切回 Idle，
            // 与 ExecuteInteract / ExecuteSelect / ExecuteInput / StopMovement 守卫语法对齐。
            if (!IsImmovable)
                ChangeState("Idle");
        }

        private void ExecuteInteractAction(ActionSequenceRuntime actionSequenceRuntime)
        {
            // Dead 状态下禁止 ActionSequence 的交互
            if (IsDead)
            {
                var failedRuntime0 = actionSequenceRuntime.GetCurActionRuntime();
                failedRuntime0.Result ??= new ActionResult();
                failedRuntime0.Result.Message = "[动作中断]你已经死了，无法执行交互。";
                failedRuntime0.State = ActionState.Failed;
                this.mCurActionRuntime = null;
                OnActionFinished(failedRuntime0);
                return;
            }
            var curAction = actionSequenceRuntime.GetCurActionStep();
            this.mCurActionRuntime = actionSequenceRuntime.GetCurActionRuntime();

            if (this.mCurActionRuntime.State == ActionState.Todo)
            {
                // 获取设备信息
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(actionSequenceRuntime.SceneObjSnap);

                this.mCurActionRuntime.StartPostion = transform.position;
                this.mCurActionRuntime.StartEnv = sceneObjsInfoDesc;
            }
            this.mCurActionRuntime.State = ActionState.Doing;

            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            // Hidden 等 IImmovableState 下不切回 Idle，以保留躲藏状态
            if (!IsImmovable)
                ChangeState("Idle");
            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.Interact(this.gameObject);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
            // 获得执行结果后，直接OnActionFinished
            if (this.mCurActionRuntime.Result == null)
                this.mCurActionRuntime.Result = new ActionResult();

            this.mCurActionRuntime.Result.Message = result;

            this.mCurActionRuntime.State = success
                ? ActionState.Done
                : ActionState.Failed;

            var finishedRuntime = this.mCurActionRuntime;
            this.mCurActionRuntime = null;

            OnActionFinished(finishedRuntime);
        }

        private void ExecuteSelectAction(ActionSequenceRuntime actionSequenceRuntime)
        {
            if (IsDead)
            {
                var failed = actionSequenceRuntime.GetCurActionRuntime();
                failed.Result ??= new ActionResult();
                failed.Result.Message = "[动作中断]你已经死了，无法执行选择。";
                failed.State = ActionState.Failed;
                this.mCurActionRuntime = null;
                OnActionFinished(failed);
                return;
            }
            var curAction = actionSequenceRuntime.GetCurActionStep();
            this.mCurActionRuntime = actionSequenceRuntime.GetCurActionRuntime();

            if (this.mCurActionRuntime.State == ActionState.Todo)
            {
                // 获取设备信息
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(actionSequenceRuntime.SceneObjSnap);

                this.mCurActionRuntime.StartPostion = transform.position;
                this.mCurActionRuntime.StartEnv = sceneObjsInfoDesc;

                
            }
            this.mCurActionRuntime.State = ActionState.Doing;

            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            // Hidden 等 IImmovableState 下不切回 Idle，以保留躲藏状态
            if (!IsImmovable)
                ChangeState("Idle");
            int selection = curAction.Select.Selection;
            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.Select(this.gameObject, selection);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
            // 获得执行结果后，直接OnActionFinished
            if (this.mCurActionRuntime.Result == null)
                this.mCurActionRuntime.Result = new ActionResult();

            this.mCurActionRuntime.Result.Message = result;

            this.mCurActionRuntime.State = success
                ? ActionState.Done
                : ActionState.Failed;

            var finishedRuntime = this.mCurActionRuntime;
            this.mCurActionRuntime = null;

            OnActionFinished(finishedRuntime);
        }

        private void ExecuteInputAction(ActionSequenceRuntime actionSequenceRuntime)
        {
            if (IsDead)
            {
                var failed = actionSequenceRuntime.GetCurActionRuntime();
                failed.Result ??= new ActionResult();
                failed.Result.Message = "[动作中断]你已经死了，无法执行输入。";
                failed.State = ActionState.Failed;
                this.mCurActionRuntime = null;
                OnActionFinished(failed);
                return;
            }
            var curAction = actionSequenceRuntime.GetCurActionStep();
            this.mCurActionRuntime = actionSequenceRuntime.GetCurActionRuntime();

            if (this.mCurActionRuntime.State == ActionState.Todo)
            {
                // 获取设备信息
                string sceneObjsInfoDesc = this.GetSceneObjSnapInfo(actionSequenceRuntime.SceneObjSnap);

                this.mCurActionRuntime.StartPostion = transform.position;
                this.mCurActionRuntime.StartEnv = sceneObjsInfoDesc;
            }
            this.mCurActionRuntime.State = ActionState.Doing;

            // 重置初始接触物体信息
            foreach (var obj in mTouchingObjs)
                this.mCurActionRuntime.StartTouchingObjs.Add(obj);
            // Hidden 等 IImmovableState 下不切回 Idle，以保留躲藏状态
            if (!IsImmovable)
                ChangeState("Idle");
            string inputText = curAction.Input.InputText;
            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.TextInput(this.gameObject, inputText);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
            // 获得执行结果后，直接OnActionFinished
            if (this.mCurActionRuntime.Result == null)
                this.mCurActionRuntime.Result = new ActionResult();

            this.mCurActionRuntime.Result.Message = result;

            this.mCurActionRuntime.State = success
                ? ActionState.Done
                : ActionState.Failed;

            var finishedRuntime = this.mCurActionRuntime;
            this.mCurActionRuntime = null;

            OnActionFinished(finishedRuntime);
        }

        private void OnCurrentActionCompleted()
        {
            if (mCurActionSequenceRuntime?.State == ActionSequenceState.Executing)
            {
                // 发送进度消息
                Debug.Log($"[{this.Name}] 动作 {mCurActionSequenceRuntime.CurActionIndex} 完成");
                // 移动到下一个动作
                mCurActionSequenceRuntime.CurActionIndex++;
                this.ExecuteCurAction();
            }
        }

        private void CompleteActionSequence()
        {
            // 设置mCurActionSequenceRuntime状态
            mCurActionSequenceRuntime.State = ActionSequenceState.Completed;
            // 组装完成消息
            if (mCurActionSequenceRuntime.ActionRuntimeLog != null && mCurActionSequenceRuntime.ActionRuntimeLog.Count > 0)
            {
                string actionSequenceLog = "";
                actionSequenceLog += $"\n=== 开始环境 ===\n{mCurActionSequenceRuntime.ActionRuntimeLog[0].StartEnv}\n";

                for (int i = 0; i < mCurActionSequenceRuntime.ActionRuntimeLog.Count; i++)
                {
                    var actionRuntime = mCurActionSequenceRuntime.ActionRuntimeLog[i];
                    actionSequenceLog += $"\n--- 动作[{i}]: {actionRuntime.ActionName} (结束条件: {actionRuntime.CompleteCondition}) ---\n";
                    actionSequenceLog += $"--- 动作[{i}]结束环境 ---\n{actionRuntime.EndEnv}\n";
                }

                string messageToSend = $"[动作序列执行结果] 动作序列已执行完成！\n<动作序列日志>{actionSequenceLog}<\\动作序列日志>{ACTION_SEQUENCE_REVIEW_PROMPT}";

                // 发送完成反馈
                this.SendFeedbackToAgent(messageToSend);
            } 
        }
        #endregion
    }
}

