using FrameworkDesign;
using Services;
using SkillBridge.Message;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class PlayerController : CharaBase
    {
        public override string Name => "玩家";
        public override string Desc => "人类玩家";

        private Rigidbody2D mRigidbody2D;
        public float moveSpeed = 5f;
        private bool moveRight;

        //private UIChat mUIChat;

        // ===== 输入模式 =====

        private enum PlayerMode
        {
            Free,
            Chatting
        }

        private PlayerMode mMode = PlayerMode.Free;

        // ===== UI =====

        
        protected override void Awake()
        {
            base.Awake();
            mRigidbody2D = GetComponent<Rigidbody2D>();
            //mUIChat = FindObjectOfType <UIChat>();
        }

        protected override void Update()
        {
            base.Update();
            GetInput();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
        }

        private void GetInput()
        {
            //// ===== 打开 / 关闭聊天 =====

            //if (Input.GetButtonDown("ToggleChat"))
            //{
            //    ToggleChat();
            //    return;
            //}

            // ===== 聊天模式禁止操作 =====

            if (mMode == PlayerMode.Chatting)
            {
                return;
            }

            // ===== 移动 =====

            float horizontal = Input.GetAxisRaw("Horizontal");
            if (horizontal != 0)
            {
                moveRight = horizontal > 0;
                ChangeState("Move");
            }
            else
            {
                ChangeState("Idle");
            }

            // ===== 交互 =====

            if (Input.GetButtonDown("Interact"))
            {
                InteractNearestDevice();
            }
        }

        //private void ToggleChat()
        //{
        //    if (mMode == PlayerMode.Free)
        //    {
        //        mMode = PlayerMode.Chatting;
                
        //        mUIChat.Open();
        //        return;
        //    }

        //    if (mMode == PlayerMode.Chatting)
        //    {
        //        mMode = PlayerMode.Free;
        //        return;
        //    }
        //}

        public void ToggleChatMode()
        {
            if (mMode == PlayerMode.Free)
            {
                mMode = PlayerMode.Chatting;
            }
        }

        public void ToggleMoveMode()
        {
            if (mMode == PlayerMode.Chatting)
            {
                mMode = PlayerMode.Free;
            }
        }

        // =========================
        // FSM Hook
        // =========================

        public override void OnIdleEnter()
        {
            mRigidbody2D.velocity =
                new Vector2(0, mRigidbody2D.velocity.y);
        }

        public override void OnMoveEnter()
        {
            float dir = moveRight ? 1f : -1f;

            TurnBack(dir);
        }

        public override void OnMoveFixedUpdate()
        {
            float dir = moveRight ? 1f : -1f;

            mRigidbody2D.velocity =
                new Vector2(dir * moveSpeed, mRigidbody2D.velocity.y);
        }

        public override void OnMoveExit()
        {
            mRigidbody2D.velocity =
                new Vector2(0, mRigidbody2D.velocity.y);
        }

        private void TurnBack(float dir)
        {
            if (dir < 0 && transform.localScale.x > 0
                || dir > 0 && transform.localScale.x < 0)
            {
                var scale = transform.localScale;

                scale.x = -scale.x;

                transform.localScale = scale;
            }
        }

        // =========================
        // 交互最近设备
        // =========================

        private void InteractNearestDevice()
        {
            if (SceneObjManager.Instance == null)
                return;

            (bool success, string result) =
                SceneObjManager.Instance.Interact(this.gameObject);

            Debug.Log(result);
        }

        // =========================
        // 聊天发送
        // =========================

        public void SendChatMessage(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            AgentService.Instance.SendUserMessage(
                "玩家",
                text
            );
        }
    }
}