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
            // ===== 聊天模式禁止操作 =====
            if (mMode == PlayerMode.Chatting)
            {
                return;
            }

            // ===== 不可移动状态（Hidden / Dead 等实现 IImmovableState 的状态） =====
            // 注：Dead 已被 Update 入口的 IsDead 提前 return；这里主要拦 Hidden 等其它不可移动状态。
            // 仍允许按 Interact（用于从柜子里出来等）。
            if (IsImmovable)
            {
                if (Input.GetButtonDown("Interact"))
                {
                    DoInteract();
                }
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
                DoInteract();
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

        private void DoInteract()
        {
            if (SceneObjManager.Instance == null)
                return;

            (bool success, string result, InteractAnimTag animTag) = SceneObjManager.Instance.Interact(this.gameObject);
            if (mPlayerAnimator != null)
                mPlayerAnimator.PlayOneShotByTag(animTag);
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
