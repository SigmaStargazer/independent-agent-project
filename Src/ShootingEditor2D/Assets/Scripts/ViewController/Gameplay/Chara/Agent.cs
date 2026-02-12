using FrameworkDesign;
using Newtonsoft.Json;
using Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace ShootingEditor2D
{
    public class Agent : CharaBase
    //public class Agent : ShootingEditor2DController
    {
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

        public override string Name => "小明";
        public override string Desc => "是一个帮助机器人";

        protected override void Awake()
        {
            base.Awake();
            mRigidbody2D = GetComponent<Rigidbody2D>();
            mGroundCheck = transform.Find("GroundCheck").GetComponent<Trigger2DCheck>();
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
        }

        protected virtual void OnDisable()
        {
            if (AgentManager.Instance != null)
                AgentManager.Instance.UnRegister(this);
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

        /// <summary>
        /// OnActionFinished钩子逻辑：当Action结束且存在finishedCtx.Result.Message时，发送消息给llm
        /// </summary>
        /// <param name="finishedCtx"></param>
        protected override void OnActionFinished(ActionContext finishedCtx)
        {
            if (finishedCtx?.Result?.Message != null)
            {
                SendMessageToAgent(finishedCtx.Result.Message);
            }
        }

        private void GetInput()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                this.Interact();
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
        /// 获取DevicesInfo
        /// </summary>
        /// <returns></returns>
        private (List<Dictionary<string, object>> devicesInfo, string devicesInfoDesc) GetDevicesInfo()
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

            (devicesInfo, interactableDeviceInfo) = deviceManager.GetDevicesInfo(this.gameObject);

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
            string speed_x_str = deviceInfo["speedDir_x"] == "" ? $"{deviceInfo["speed_x"]}m/s" : $"方向{deviceInfo["speedDir_x"]} {deviceInfo["speed_x"]}m/s";
            string speed_y_str = deviceInfo["speedDir_y"] == "" ? $"{deviceInfo["speed_y"]}m/s" : $"方向{deviceInfo["speedDir_y"]} {deviceInfo["speed_y"]}m/s";
            string deviceInfoDesc = $"{deviceInfo["name"]}: {deviceInfo["desc"]}\n" +
                $"状态:{deviceInfo["state"]}\n" +
                $"方向:{deviceInfo["direction"]}\n距离:{deviceInfo["distance"]}m\n" +
                $"横向速度:{speed_x_str}\n纵向速度:{speed_y_str}";
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
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo();

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
            curActionCtx = new ActionContext
            {
                ActionName = "Move",
                Result = new ActionResult(),
                startPostion = new System.Numerics.Vector2(startX, transform.position.y),
                EndCondition = () =>
                {
                    float dir = moveRight ? 1f : -1f;
                    float targetX = startX + dir * distance;

                    bool arrived = moveRight
                        ? transform.position.x >= targetX
                        : transform.position.x <= targetX;

                    if (arrived)
                    {
                        curActionCtx.Result.Message = "[移动结果]到达目的地！";
                    }

                    return arrived;
                }
            };

            ChangeState("Move");
        }

        //private void Move(bool moveRight, float distance)
        //{
        //    Debug.Log($"开始移动: moveRight={moveRight} distance={distance}");
        //    MoveByDistance(moveRight, distance);
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
            (devicesInfo, devicesInfoDesc) = this.GetDevicesInfo();

            // 拼接
            string messageToSend = $"[观察结果]<环境>\n{devicesInfoDesc}\n<\\环境>";

            // 发送给Agent
            AgentService.Instance.SendUserMessage(this.Name, messageToSend);
            Debug.Log($"已发送消息给{this.Name}: {messageToSend}");
        }
        #endregion
    }
}

