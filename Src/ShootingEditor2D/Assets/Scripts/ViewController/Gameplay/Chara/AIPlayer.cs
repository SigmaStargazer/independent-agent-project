using FrameworkDesign;
using Newtonsoft.Json;
using Services;
using SkillBridge.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.VisualScripting;
using UnityEditor.U2D.Path.GUIFramework;
using UnityEngine;

namespace ShootingEditor2D
{
    public class AIPlayer : PlayerBase
    //public class Agent : ShootingEditor2DController
    {
        public override string Name => "小明";
        public override string Desc => "是一个帮助机器人";

        private Rigidbody2D mRigidbody2D;
        private Trigger2DCheck mGroundCheck;
        //private Gun mGun;
        //public float isRight;

        // move相关
        public float moveSpeed = 5f;

        private bool moveRight;
        private bool moveFinished;
        //private float moveDistance;
        //private float moveStartX;
        //private float moveTargetX;

        // 用于检查撞击场景对象时停止
        private HashSet<SceneObjBase> mTouchingObjs = new HashSet<SceneObjBase>();

        //private ActionSequenceRuntime mCurActionSequenceRuntime = new ActionSequenceRuntime();
        private ActionSequenceRuntime mCurActionSequenceRuntime;
        private ActionSequenceRuntime mPlanningActionSequenceRuntime;

        private ConditionEvaluator mConditionEvaluator;

        protected override void Awake()
        {
            base.Awake();
            mRigidbody2D = GetComponent<Rigidbody2D>();
            mGroundCheck = transform.Find("GroundCheck").GetComponent<Trigger2DCheck>();
            mConditionEvaluator = new ConditionEvaluator();
            //mGun = transform.Find("Gun").GetComponent<Gun>();
        }
        protected override void Start()
        {
            base.Start();
        }

        protected override void Update()
        {
            base.Update();
        }


        protected override void FixedUpdate()
        {
            base.FixedUpdate();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (AgentManager.Instance != null)
                AgentManager.Instance.Register(this);
            // 把加入ActionSequenceRuntime.AddSceneObj的方法，注册到委托SceneObjManager.OnSceneObjCreated
            // 当SceneObjBase创建时，调用SceneObjManager.Register，触发委托OnSceneObjCreated
            SceneObjManager.OnSceneObjCreated += OnSceneObjCreated;

            // 当Agent后于SceneObjBase创建时，确保所有SceneObj被存入ActionSequenceRuntime.sceneObjsSnap中
            if (SceneObjManager.Instance != null)
            {
                foreach (var sceneObj in SceneObjManager.Instance.GetSceneObjsExcluding(this.gameObject))
                {
                    OnSceneObjCreated(sceneObj);
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (AgentManager.Instance != null)
                AgentManager.Instance.UnRegister(this);

            SceneObjManager.OnSceneObjCreated -= OnSceneObjCreated;
        }
        #region FSM Hook
        public override void OnIdleEnter()
        {
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }
        public override void OnMoveEnter()
        {
            moveFinished = false;
            //moveStartX = transform.position.x;
            float dir = moveRight ? 1f : -1f;
            //moveTargetX = moveStartX + dir * moveDistance;

            // 校正朝向
            TurnBack(dir);
        }

        public override void OnMoveFixedUpdate()
        {
            if (moveFinished) return;

            float dir = moveRight ? 1f : -1f;

            // 持续移动
            mRigidbody2D.velocity = new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
        }

        public override void OnMoveExit()
        {
            // 刹车
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        }

        #endregion

        private void OnSceneObjCreated(SceneObjBase obj)
        {
            mCurActionSequenceRuntime?.AddSceneObj(obj);
            mPlanningActionSequenceRuntime?.AddSceneObj(obj);
        }

        /// <summary>
        /// OnActionFinished钩子逻辑：当Action结束且存在finishedCtx.Result.Message时，发送消息给llm
        /// </summary>
        /// <param name="finishedActionRuntime"></param>
        protected override void OnActionFinished(ActionRuntime finishedActionRuntime)
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
                    // 发送取消信息
                    string result = finishedActionRuntime.Result.Message;
                    this.SendMessageToAgent($"[动作序列执行中断]{result}");

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
                    this.SendMessageToAgent(finishedActionRuntime.Result.Message);
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
        private string GetSelfStateInfo()
        {
            Rigidbody2D rb = this.GetComponent<Rigidbody2D>();
            Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;
            string speedDirX = velocity.x > 0.01f ? "right" : (velocity.x < -0.01f ? "left" : "");
            string speedDirY = velocity.y > 0.01f ? "up" : (velocity.y < -0.01f ? "down" : "");

            string speed_x_str = speedDirX == "" ? $"{Mathf.Abs(velocity.x)}m/s" : $"方向{speedDirX} {Mathf.Abs(velocity.x)}m/s";
            string speed_y_str = speedDirY == "" ? $"{Mathf.Abs(velocity.y)}m/s" : $"方向{speedDirY} {Mathf.Abs(velocity.y)}m/s";

            var actionInfoRenderer = new ActionInfoRenderer();

            // 拼接返回字符串
            string selfStateInfo = $"# 状态:{this.GetStateName()}" + 
                $"\n# 横向速度:{speed_x_str}\n# 纵向速度:{speed_y_str}" +
                $"\n# 计划中的动作序列:\n{actionInfoRenderer.RenderActionSequenceRuntime(this.mPlanningActionSequenceRuntime)}" +
                $"\n# 进行中的动作序列:\n{actionInfoRenderer.RenderActionSequenceRuntime(this.mCurActionSequenceRuntime)}" +
                $"\n# 进行中的动作:\n{actionInfoRenderer.RenderActionRuntime(this.mCurActionRuntime)}";

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
        /// 发送消息给Agent
        /// </summary>
        /// <param name="msg"></param>
        public void SendMessageToAgent(string msg)
        {
            // 获取环境信息
            List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
            string selfStateInfo = this.GetSelfStateInfo();
            string sceneObjsInfoDesc = this.GetEnvSceneObjsInfo();

            // 拼接
            string messageToSend = $"{msg}" +
                $"\n\n<你的状态>\n{selfStateInfo}\n<\\你的状态>" + 
                $"\n\n<环境>\n{sceneObjsInfoDesc}\n<\\环境>";

            // 发送给Agent
            AgentService.Instance.SendUserMessage(this.Name, messageToSend);
            // 测试用
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        #region Agent动作指令。当AgentManager收到服务端LLM的指令时，会调用相应Agent示例的下列方法
        /// <summary>
        /// 移动
        /// </summary>
        /// <param name="moveRight"></param>
        /// <param name="distance"></param>
        /// 
        public void Move(bool moveRight, float distance)
        {
            this.moveRight = moveRight;

            float startX = transform.position.x;
            //this.moveDistance = distance;
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
        }

        /// <summary>
        /// 交互
        /// </summary>
        public void Interact(string requestId)
        {
            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("场景中未找到 SceneObjManager！");
                return;
            }
            (bool success, string result) = SceneObjManager.Instance.Interact(this.gameObject);
            string messageToSend = $"[交互结果]{result}";
            AgentService.Instance.SendToolResultMessage(this.Name, "Interact", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        public void Select(int selection, string requestId)
        {
            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("场景中未找到 SceneObjManager！");
                return;
            }
            (bool success, string result) = SceneObjManager.Instance.Select(this.gameObject, selection);
            string messageToSend = $"[选择结果]{result}";
            AgentService.Instance.SendToolResultMessage(this.Name, "Select", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }

        public void TextInput(string inputText, string requestId)
        {
            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("场景中未找到 SceneObjManager！");
                return;
            }
            (bool success, string result) = SceneObjManager.Instance.TextInput(this.gameObject, inputText);
            string messageToSend = $"[输入结果]{result}";
            AgentService.Instance.SendToolResultMessage(this.Name, "TextInput", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }



        /// <summary>
        /// 观察场景
        /// </summary>
        public void Observe(string requestId)
        {
            // 获取设备信息
            List<Dictionary<string, object>> sceneObjsInfo = new List<Dictionary<string, object>>();
            string sceneObjsInfoDesc = this.GetEnvSceneObjsInfo();

            // 拼接
            string messageToSend = $"[观察结果]\n<环境>\n{sceneObjsInfoDesc}\n<\\环境>";

            // 发送给Agent
            // tool_name = "observe"只用于日志打印，不用于判断
            AgentService.Instance.SendToolResultMessage(this.Name, "Observe", requestId, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
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
                this.StopAction();
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
            this.StopAction();
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
                        if (!this.mCurActionRuntime.StartTouchingObjs.Contains(obj))
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
            ChangeState("Idle");
        }

        private void ExecuteInteractAction(ActionSequenceRuntime actionSequenceRuntime)
        {
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
            // 执行一次
            ChangeState("Idle");
            (bool success, string result) = SceneObjManager.Instance.Interact(this.gameObject);
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
            // 执行一次
            ChangeState("Idle");
            int selection = curAction.Select.Selection;
            (bool success, string result) = SceneObjManager.Instance.Select(this.gameObject, selection);
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
            // 执行一次
            ChangeState("Idle");
            string inputText = curAction.Input.InputText;
            (bool success, string result) = SceneObjManager.Instance.TextInput(this.gameObject, inputText);
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

                string messageToSend = $"[动作序列执行结果] 动作序列已执行完成！\n<动作序列日志>{actionSequenceLog}<\\动作序列日志>";

                // 发送完成消息
                this.SendMessageToAgent(messageToSend);
            } 
        }
        #endregion
    }
}

