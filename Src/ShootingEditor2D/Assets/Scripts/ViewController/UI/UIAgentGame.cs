using Services;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SkillBridge.Message;

using UnityEngine.SceneManagement;

namespace ShootingEditor2D
{
    public class UIAgentGame : MonoBehaviour
    {
        public InputField messageInputField;
        public Text aiMessageText;
        public Agent chara;
        // Start is called before the first frame update
        void Start()
        {
            AgentService.Instance.OnGetAgentMessage = this.OnGetAgentMessage;
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void OnClickSendMessage()
        {
            // 发送消息
            if (messageInputField != null && !string.IsNullOrEmpty(messageInputField.text) && chara != null)
            {
                string userMessage = messageInputField.text;
                chara.SendMessageToAgent($"用户: {userMessage}");
                //// 获取环境信息
                //List<Dictionary<string, object>> devicesInfo = new List<Dictionary<string, object>>();
                //string devicesInfoDesc = "";
                //(devicesInfo, devicesInfoDesc) = chara.GetDevicesInfo();

                //// 获取输入框的内容
                //string userMessage = messageInputField.text;

                //// 拼接
                //string messageToSend = $"{devicesInfoDesc}\n\n用户向你发送了一则消息: {userMessage}";

                //// 发送给小明
                //AgentService.Instance.SendUserMessage("小明", messageToSend);

                // 清空输入框
                messageInputField.text = "";

                //Debug.Log($"已发送消息给小明: {messageToSend}");
            }
            else
            {
                Debug.LogWarning("输入框未绑定/内容为空/chara为空！");
            }
        }
        private void OnGetAgentMessage(string agent, string ai_message)
        {
            aiMessageText.text = $"{agent}: {ai_message}";
        }


    }

}


