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
using UnityEngine;

namespace ShootingEditor2D
{
    public class Agent : CharaBase
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


        // 本帧是否按了跳
        private bool mJumpPressed;

        //// 【新增】是否正在进行自动移动（用于屏蔽玩家输入）
        //private bool mIsAutoMoving = false;

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
            //AgentService.Instance.OnObserve = this.Observe;
            //AgentService.Instance.OnMoveAgent = this.Move;
            //AgentService.Instance.OnInteract = this.Interact;
            //AgentService.Instance.OnSelect = this.Select;
            //AgentService.Instance.OnInput = this.TextInput;
        }

        protected override void Update()
        {
            base.Update();
            GetInput();
        }


        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            //// 【修改点】如果正在自动移动，直接跳过玩家输入的处理
            //if (mIsAutoMoving)
            //{
            //    return;
            //}

            //var rawInput = Input.GetAxis("Horizontal");
            //float horizontalDirection = 0;
            //if (Mathf.Abs(rawInput) > 0.01f)
            //{
            //    horizontalDirection = Mathf.Sign(rawInput);
            //}

            //isRight = Mathf.Sign(transform.localScale.x);

            //TurnBack(horizontalDirection);
            //MoveAndJump(horizontalDirection);
        }

        protected virtual void OnEnable()
        {
            if (AgentManager.Instance != null)
                AgentManager.Instance.Register(this);
            // 把加入ActionSequenceRuntime.AddSceneObj的方法，注册到委托DeviceManager.OnDeviceCreated
            // 当DeviceBase创建时，调用DeviceManager.Register，触发委托OnDeviceCreated
            DeviceManager.OnDeviceCreated += OnDeviceCreated;

            // 当Agent后于DeviceBase创建时，确保所有SceneObj被存入ActionSequenceRuntime.sceneObjsSnap中
            if (DeviceManager.Instance != null)
            {
                foreach (var device in DeviceManager.Instance.GetDevices())
                {
                    OnDeviceCreated(device);
                }
            }
        }

        protected virtual void OnDisable()
        {
            if (AgentManager.Instance != null)
                AgentManager.Instance.UnRegister(this);

            DeviceManager.OnDeviceCreated -= OnDeviceCreated;
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

        private void OnDeviceCreated(DeviceBase obj)
        {
            mCurActionSequenceRuntime?.AddDevice(obj);
            mPlanningActionSequenceRuntime?.AddDevice(obj);
        }

        /// <summary>
        /// OnActionFinished钩子逻辑：当Action结束且存在finishedCtx.Result.Message时，发送消息给llm
        /// </summary>
        /// <param name="finishedActionRuntime"></param>
        protected override void OnActionFinished(ActionRuntime finishedActionRuntime)
        {
            if (finishedActionRuntime == null)
                return;

            // 1. 获取当前Action的EndEnv
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            string devicesInfoDesc = "";

            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo(mCurActionSequenceRuntime.DeviceSnap);
            finishedActionRuntime.EndEnv = devicesInfoDesc;
            // 2.执行Action结束逻辑
            // 如果执行的是ActionSequence中的Action
            if (mCurActionSequenceRuntime?.State == ActionSequenceState.Executing)
            {
                OnCurrentActionCompleted();
                return;
            }
            // 如果执行的是非ActionSequence中的Action
            else
            {
                if (finishedActionRuntime?.Result?.Message != null)
                {
                    SendMessageToAgent(finishedActionRuntime.Result.Message);
                }
            }
        }

        private void GetInput()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                this.TestSceneObjsSnap();
            }
            //if (Input.GetKeyDown(KeyCode.Space))
            //{
            //    mJumpPressed = true;
            //}
            ////if (Input.GetKeyDown(KeyCode.J))
            ////{
            ////    mGun.Shoot();
            ////}
            ////if (Input.GetKeyDown(KeyCode.R))
            ////{
            ////    mGun.Reload();
            ////}
            //if (Input.GetKeyDown(KeyCode.Q))
            //{
            //    this.SendCommand<ShiftGunCommand>();
            //}
        }

        private void TurnBack(float horizontalDirection)
        {
            if (horizontalDirection < 0 && transform.localScale.x > 0
                || horizontalDirection > 0 && transform.localScale.x < 0)
            {
                var localScale = transform.localScale;
                localScale.x = -localScale.x;
                transform.localScale = localScale;
            }
        }

        //private void MoveAndJump(float horizontalDirection)
        //{
        //    mRigidbody2D.velocity = new Vector2(horizontalDirection * moveSpeed, mRigidbody2D.velocity.y);

        //    var grounded = mGroundCheck.Triggered;

        //    if (mJumpPressed && grounded)
        //    {
        //        mRigidbody2D.velocity = new Vector2(mRigidbody2D.velocity.x, 5);
        //    }
        //    mJumpPressed = false;
        //}

        /// <summary>
        /// 移动
        /// </summary>
        /// <param name="moveRight"></param>
        /// <param name="distance"></param>
        //public void MoveByDistance(bool moveRight, float distance)
        //{
        //    // 停止之前的移动协程（防止多次调用冲突）
        //    StopAllCoroutines();
        //    StartCoroutine(MoveDistanceCoroutine(moveRight, distance));
        //}

        //private IEnumerator MoveDistanceCoroutine(bool moveRight, float distance)
        //{
        //    mIsAutoMoving = true; // 锁定输入

        //    float startX = transform.position.x;
        //    float directionSign = moveRight ? 1f : -1f;
        //    float targetX = startX + (distance * directionSign);

        //    // 确保朝向正确
        //    TurnBack(directionSign);

        //    // 循环直到到达目标位置
        //    // 判断条件：如果是向右走，当前x小于目标x；如果是向左走，当前x大于目标x
        //    while ((moveRight && transform.position.x < targetX) ||
        //           (!moveRight && transform.position.x > targetX))
        //    {
        //        // 保持物理移动速度
        //        mRigidbody2D.velocity = new Vector2(directionSign * moveSpeed, mRigidbody2D.velocity.y);

        //        // 等待下一次物理帧
        //        yield return new WaitForFixedUpdate();
        //    }

        //    // 到达目标，刹车
        //    mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);
        //    mIsAutoMoving = false; // 恢复输入
        //    // 向agent反馈
        //    SendMessageToAgent("[移动结果]到达目的地！");
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
            string selfStateInfo = $"状态:{this.GetStateName()}\n" + 
                $"横向速度:{speed_x_str}\n纵向速度:{speed_y_str}";

            return selfStateInfo;
        }

        /// <summary>
        /// 获取设备信息列表DevicesInfo，以及转化为的文字描述devicesInfoDesc
        /// </summary>
        /// <returns></returns>
        private (List<Dictionary<string, object>> devicesInfo, string devicesInfoDesc) GetDevicesInfo(List<DeviceBase> devices)
        {
            string devicesInfoDesc = "";

            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            Dictionary<string, object> interactableDeviceInfo = new Dictionary<string, object>();

            if (deviceManager == null)
            {
                Debug.LogError("场景中未找到 DeviceManager！");
                return (devicesInfo, "");
            }

            (devicesInfo, interactableDeviceInfo) = deviceManager.GetDevicesInfo(this.gameObject, devices);

            if (devicesInfo.Count > 0)
            {
                devicesInfoDesc = "你的周围有：";
                int deviceId = 0;
                // 1.遍历设备信息
                foreach (var deviceInfo in devicesInfo)
                {
                    string deviceInfoDesc = $"\n{deviceId}. {DeviceInfoToDesc(deviceInfo)}";

                    devicesInfoDesc += deviceInfoDesc;
                    deviceId++;
                }

                // 2.获取可交互设备信息
                string interactableDevicDesc = "\n\n可选择交互：\n";
                if (interactableDeviceInfo != null && interactableDeviceInfo.Count > 0)
                {
                    interactableDevicDesc += $"{DeviceInfoToDesc(interactableDeviceInfo)}";
                }
                else
                {
                    interactableDevicDesc += "身边无可交互对象";
                }
                devicesInfoDesc += interactableDevicDesc;
            }

            return (devicesInfo, devicesInfoDesc);
        }

        private string DeviceInfoToDesc(Dictionary<string, object> deviceInfo)
        {
            string deviceInfoDesc = "";
            if (deviceInfo != null)
            {
                string speed_x_str = deviceInfo["speedDir_x"] == "" ? $"{deviceInfo["speed_x"]}m/s" : $"方向{deviceInfo["speedDir_x"]} {deviceInfo["speed_x"]}m/s";
                string speed_y_str = deviceInfo["speedDir_y"] == "" ? $"{deviceInfo["speed_y"]}m/s" : $"方向{deviceInfo["speedDir_y"]} {deviceInfo["speed_y"]}m/s";
                deviceInfoDesc = $"{deviceInfo["name"]}: {deviceInfo["desc"]}\n" +
                    $"状态:{deviceInfo["state"]}\n" +
                    $"方向:{deviceInfo["direction"]}\n距离:{deviceInfo["distance"]}m\n" +
                    $"横向速度:{speed_x_str}\n纵向速度:{speed_y_str}";
            }
            else
            {
                deviceInfoDesc = "物体已消失";
            }
                return deviceInfoDesc;
        }

        /// <summary>
        /// 发送消息给Agent
        /// </summary>
        /// <param name="msg"></param>
        public void SendMessageToAgent(string msg)
        {
            // 获取环境信息
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            string selfStateInfo = this.GetSelfStateInfo();
            string devicesInfoDesc = "";
            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            var mDevices = deviceManager.GetDevices();
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo(mDevices);

            // 拼接
            string messageToSend = $"{msg}" +
                $"\n\n<你的状态>\n{selfStateInfo}\n<\\你的状态>" + 
                $"\n\n<环境>\n{devicesInfoDesc}\n<\\环境>";

            // 发送给Agent
            AgentService.Instance.SendUserMessage(this.Name, messageToSend);
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
            curActionRuntime = new ActionRuntime
            {
                ActionName = "Move",
                Result = new ActionResult(),
                StartPostion = new Vector2(startX, transform.position.y),
                CompleteConditionFunc = () =>
                {
                    float dir = moveRight ? 1f : -1f;

                    bool arrived = curActionRuntime.Displacement >= distance;

                    if (arrived)
                    {
                        curActionRuntime.Result.Message = "[移动结果]到达目的地！";
                    }

                    return arrived;
                }
            };

            ChangeState("Move");
        }

        //public void Move(bool moveRight, float distance)
        //{
        //    this.moveRight = moveRight;

        //    float startX = transform.position.x;
        //    //this.moveDistance = distance;
        //    curActionRuntime = new ActionRuntime
        //    {
        //        ActionName = "Move",
        //        Result = new ActionResult(),
        //        StartPostion = new Vector2(startX, transform.position.y),
        //        CompleteConditionFunc = () =>
        //        {
        //            float dir = moveRight ? 1f : -1f;
        //            float targetX = startX + dir * distance;

        //            bool arrived = moveRight
        //                ? transform.position.x >= targetX
        //                : transform.position.x <= targetX;

        //            if (arrived)
        //            {
        //                curActionRuntime.Result.Message = "[移动结果]到达目的地！";
        //            }

        //            return arrived;
        //        }
        //    };

        //    ChangeState("Move");
        //}

        /// <summary>
        /// 交互
        /// </summary>
        public void Interact()
        {
            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            if (deviceManager == null)
            {
                Debug.LogError("场景中未找到 DeviceManager！");
                return;
            }
            string result = deviceManager.Interact(this.gameObject);
            this.SendMessageToAgent($"[交互结果]{result}");
        }

        public void Select(int selection)
        {
            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            if (deviceManager == null)
            {
                Debug.LogError("场景中未找到 DeviceManager！");
                return;
            }
            string result = deviceManager.Select(this.gameObject, selection);
            this.SendMessageToAgent($"[选择结果]{result}");
        }

        public void TextInput(string inputText)
        {
            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            if (deviceManager == null)
            {
                Debug.LogError("场景中未找到 DeviceManager！");
                return;
            }
            string result = deviceManager.TextInput(this.gameObject, inputText);
            this.SendMessageToAgent($"[输入结果]{result}");
        }



        /// <summary>
        /// 观察场景
        /// </summary>
        public void Observe()
        {
            // 获取设备信息
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            string devicesInfoDesc = "";
            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            var mDevices = deviceManager.GetDevices();
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo(mDevices);

            // 拼接
            string messageToSend = $"[观察结果]<环境>\n{devicesInfoDesc}\n<\\环境>";

            // 发送给Agent
            AgentService.Instance.SendUserMessage(this.Name, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }
        #endregion

        #region ActionSequence相关
        public void PlanActionSequence(List<ActionStep> actionSequence)
        {
            if (actionSequence == null || actionSequence.Count == 0)
            {
                Debug.Log("ActionSequence为空！");
                this.SendMessageToAgent("ActionSequence为空！");// 这块尽量在服务端处理
                return;
            }
            Debug.Log($"[Agent] 收到动作序列规划请求，共 {actionSequence.Count} 个动作");

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
                this.mPlanningActionSequenceRuntime = new ActionSequenceRuntime();
                this.mPlanningActionSequenceRuntime.ActionSequence = actionSequence;
                foreach (var device in DeviceManager.Instance.GetDevices())
                {
                    this.mPlanningActionSequenceRuntime.AddDevice(device);
                }

                // 2. 生成 ConditionContext 快照
                List<SceneObjBase> sceneObjs = new List<SceneObjBase>();
                sceneObjs.AddRange(mPlanningActionSequenceRuntime.DeviceSnap);// 以后再追加其他chara
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
                    this.SendMessageToAgent($"[ActionSequence规划结果]action_sequence校验未通过:\n{combinedError}");
                    return;
                }
                else // 4.生成确认信息
                {
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[{this.Name}] 动作序列规划出错：{e}");
                this.SendMessageToAgent($"[ActionSequence规划结果]出错：{e.Message}");
            }

        }

        public void StartActionSequence()
        {
            if (mPlanningActionSequenceRuntime == null)
            {
                Debug.Log($"[{this.Name}]开始ActionSequence失败: 没有计划中的ActionSequence");
                this.SendMessageToAgent($"开始ActionSequence失败: 没有计划中的ActionSequence");
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
                Debug.Log($"[{this.Name}]开始ActionSequence成功: {this.mCurActionSequenceRuntime.ActionSequence.Count}个Action");
                this.SendMessageToAgent($"开始ActionSequence成功: {this.mCurActionSequenceRuntime.ActionSequence.Count}个Action");
            }
            catch (Exception e)
            {
                Debug.Log($"[{Name}]开始ActionSequence失败: {e.Message}");
                this.SendMessageToAgent($"开始ActionSequence失败: {e.Message}");
                return;
            }
        }

        public void CancelActionSequence()
        {
            if (mCurActionSequenceRuntime == null)
            {
                Debug.Log($"[{this.Name}]取消ActionSequence失败: 没有执行中的ActionSequence");
                this.SendMessageToAgent($"取消ActionSequence失败: 没有执行中的ActionSequence");
                return;
            }

            // 1. 停止当前的Action
            this.StopAction();
            // 2. 清空当前的ActionSequenceRuntime
            mCurActionSequenceRuntime.Dispose();
            mCurActionSequenceRuntime = null;
            // 3. 发送取消信息
            Debug.Log($"[{this.Name}]取消ActionSequence成功");
            this.SendMessageToAgent($"取消ActionSequence成功");
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
                    this.ExecuteMoveAction(curAction);
                }
                else if (curAction.Wait != null)
                {
                    this.ExecuteWaitAction(curAction);
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
        private void ExecuteMoveAction(ActionStep curAction)
        {
            this.moveRight = curAction.Move.direction == MoveAction.Direction.Right;

            // 获取设备信息
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            string devicesInfoDesc = "";
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo(mCurActionSequenceRuntime.DeviceSnap);

            // 创建Condition判断上下文
            List<SceneObjBase> sceneObjs = new List<SceneObjBase>();
            sceneObjs.AddRange(mCurActionSequenceRuntime.DeviceSnap);// 以后再追加其他chara
            var conditionCxt = new ConditionContext(this, sceneObjs);

            curActionRuntime = new ActionRuntime
            {
                ActionName = "Move",
                Result = new ActionResult(),
                StartPostion = new Vector2(transform.position.x, transform.position.y),
                StartEnv = devicesInfoDesc,
                CompleteCondition = curAction.Condition,
                CompleteConditionFunc = () =>
                {
                    // 每帧更新动态变量
                    conditionCxt.ActionTime = curActionRuntime.ActionTime;
                    conditionCxt.Displacement = curActionRuntime.Displacement;
                    int index = mCurActionSequenceRuntime.CurActionIndex;

                    ConditionEvalResult result = mConditionEvaluator.Evaluate(index, curAction, conditionCxt);
                    // 当条件为True/Error时，停止Action
                    return result.Status != ConditionEvalStatus.False;
                }
            };
            mCurActionSequenceRuntime.AddActionRuntimeLog(curActionRuntime);

            ChangeState("Move");
        }
        private void ExecuteWaitAction(ActionStep curAction)
        {
            // 获取设备信息
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            string devicesInfoDesc = "";
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo(mCurActionSequenceRuntime.DeviceSnap);


            // 创建Condition判断上下文
            List<SceneObjBase> sceneObjs = new List<SceneObjBase>();
            sceneObjs.AddRange(mCurActionSequenceRuntime.DeviceSnap);// 以后再追加其他chara
            var conditionCxt = new ConditionContext(this, sceneObjs);

            curActionRuntime = new ActionRuntime
            {
                ActionName = "Wait",
                Result = new ActionResult(),
                StartPostion = new Vector2(transform.position.x, transform.position.y),
                StartEnv = devicesInfoDesc,
                CompleteCondition = curAction.Condition,
                CompleteConditionFunc = () =>
                {
                    // 每帧更新动态变量
                    conditionCxt.ActionTime = curActionRuntime.ActionTime;
                    conditionCxt.Displacement = curActionRuntime.Displacement;
                    int index = mCurActionSequenceRuntime.CurActionIndex;

                    ConditionEvalResult result = mConditionEvaluator.Evaluate(index, curAction, conditionCxt);
                    // 当条件为True/Error时，停止Action
                    return result.Status != ConditionEvalStatus.False;
                }
            };
            mCurActionSequenceRuntime.AddActionRuntimeLog(curActionRuntime);

            ChangeState("Idle");
        }

        private void OnCurrentActionCompleted()
        {
            if (mCurActionSequenceRuntime?.State == ActionSequenceState.Executing)
            {
                // 发送进度消息
                Debug.Log($"[Agent] 动作 {mCurActionSequenceRuntime.CurActionIndex} 完成");
                // 移动到下一个动作
                mCurActionSequenceRuntime.CurActionIndex++;
                ExecuteCurAction();
            }
        }

        private void CompleteActionSequence()
        {
            // 设置mCurActionSequenceRuntime状态
            mCurActionSequenceRuntime.State = ActionSequenceState.Completed;
            // 组装完成消息
            if (mCurActionSequenceRuntime.ActionRuntimeLog != null && mCurActionSequenceRuntime.ActionRuntimeLog.Count > 0)
            {
                string actionSequenceLog = "# ActionSequence日志:";
                actionSequenceLog += $"开始环境:" +
                    $"\n{mCurActionSequenceRuntime.ActionRuntimeLog[0].StartEnv}";
                int index = 0;
                foreach (var actionRuntime in mCurActionSequenceRuntime.ActionRuntimeLog)
                {
                    actionSequenceLog += $"\nAction[{index}]: {actionRuntime.ActionName} 结束条件: {actionRuntime.CompleteCondition}\n" +
                        $"\nAction[{index}]结束时环境:" +
                        $"\n{mCurActionSequenceRuntime.ActionRuntimeLog[index].EndEnv}";
                    index++;
                }
            }


            // 发送完成消息
        }
        #endregion

        public void TestSceneObjsSnap()
        {
            // 获取设备信息
            List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
            string devicesInfoDesc = "";
            DeviceManager deviceManager = GameObject.FindObjectOfType<DeviceManager>();
            var mDevices = deviceManager.GetDevices();
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo(mDevices);

            Debug.Log($"devicesInfoDesc: {devicesInfoDesc}");

            var sceneObjsSnap = mCurActionSequenceRuntime.DeviceSnap;
            foreach (var sceneObj in sceneObjsSnap)
            {
                if (sceneObj == null)
                {
                    Debug.Log($"sceneObj: {sceneObj.Name} 已销毁！");
                    continue;
                }
                else if (!sceneObj.gameObject.activeInHierarchy)
                {
                    Debug.Log($"sceneObj: {sceneObj.Name} 未激活！");
                }
                else
                {
                    Debug.Log($"sceneObj: {sceneObj.Name}");
                }
            }
            //Debug.Log($"sceneObjsSnap: {mCurActionSequenceRuntime.sceneObjsSnap}");
        }
    }
}

