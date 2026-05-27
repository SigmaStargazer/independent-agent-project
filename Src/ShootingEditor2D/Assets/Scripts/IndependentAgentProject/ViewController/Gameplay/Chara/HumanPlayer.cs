using FrameworkDesign;
using Services;
using SkillBridge.Message;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class HumanPlayer : PlayerBase
    {
        public override string Name => "玩家";
        public override string Desc => "人类玩家";

        //private UIChat mUIChat;

        // ===== 输入模式 =====

        private enum PlayerMode
        {
            Free,
            Chatting
        }

        private PlayerMode mMode = PlayerMode.Free;

        // ===== UI =====

        protected override void Update()
        {
            base.Update();
            if (IsDead)
                return;
            GetInput();
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
                Interact();
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
        // 交互最近设备
        // =========================

        private void Interact()
        {
            if (SceneObjManager.Instance == null)
                return;

            (bool success, string result) = SceneObjManager.Instance.Interact(this.gameObject);
            Debug.Log($"玩家交互结果::success:{success} result:{result}");
        }

        // =========================
        // 聊天发送
        // =========================

        //public void SendChatMessage(string text)
        //{
        //    if (string.IsNullOrEmpty(text))
        //        return;

        //    AgentService.Instance.SendUserMessage(
        //        "玩家",
        //        text
        //    );
        //}
    }
}