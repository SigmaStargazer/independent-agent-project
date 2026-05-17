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
    public class UIAgentStart : MonoBehaviour
    {
        public InputField nameInputField;
        public InputField descInputField;
        //public InputField messageInputField;
        //public Text aiMessageText;
        // Start is called before the first frame update
        void Start()
        {
            //AgentService.Instance.OnLoadAgent = this.OnLoadAgent;
            ////AgentService.Instance.OnGetAgentMessage = this.OnGetAgentMessage;
            //AgentService.Instance.OnStartScene = this.OnStartScene;
        }

        void OnEnable()
        {
            AgentService.Instance.OnLoadAgent += OnLoadAgent;
            AgentService.Instance.OnStartScene += OnStartScene;
        }

        // Update is called once per frame
        void Update()
        {

        }

        void OnDisable()
        {
            AgentService.Instance.OnLoadAgent -= OnLoadAgent;
            AgentService.Instance.OnStartScene -= OnStartScene;
        }
        //public void OnClickSend()
        //{
        //    // 创建Agent
        //    AgentService.Instance.SendAgentCreate("小明", "是一个帮助机器人");
        //    AgentService.Instance.SendAgentCreate("小红", "是用户的秘书");
        //    AgentService.Instance.SendSceneStart(1);
        //    AgentService.Instance.SendUserMessage("小明", "和小红说，让她闹个每天8点的起床铃，9点的上班铃声，然后每天闹铃响时让她直接通知我。");
        //}

        public void OnClickCreateAgent()
        {
            if (nameInputField != null && !string.IsNullOrEmpty(nameInputField.text) && descInputField != null && !string.IsNullOrEmpty(descInputField.text))
            {
                AgentService.Instance.SendAgentCreate(nameInputField.text, descInputField.text);
            }
            else
            {
                Debug.LogWarning("输入框未绑定或内容为空！");
            }
        }

        public void OnClickLoadAgent()
        {
            AgentService.Instance.SendAgentLoad();
        }

        void OnLoadAgent(bool success, List<string> agentNames)
        {
            if (success)
            {
                Debug.Log($"已加载的Agent: {string.Join(", ", agentNames)}");
                AgentService.Instance.SendSceneStart(1); // 加载场景
            }
            else
            {
                Debug.LogWarning("加载Agent失败！");
            }
        }

        public void OnClickStartScene()
        {
            AgentService.Instance.SendSceneStart(1);
        }

        void OnStartScene(bool success, string reason)
        {
            SceneManager.LoadScene("AgentClientTest2");
        }

        //public void OnClickSendMessage()
        //{
        //    //// 创建Agent
        //    //AgentService.Instance.SendAgentCreate("小明", "是一个帮助机器人");
        //    //AgentService.Instance.SendAgentCreate("小红", "是用户的秘书");
        //    //AgentService.Instance.SendSceneStart(1);

        //    // 发送消息
        //    if (messageInputField != null && !string.IsNullOrEmpty(messageInputField.text))
        //    {
        //        // 获取输入框的内容
        //        string userMessage = messageInputField.text;

        //        // 发送给小明
        //        AgentService.Instance.SendUserMessage("小明", userMessage);

        //        // 清空输入框
        //        messageInputField.text = "";

        //        Debug.Log($"已发送消息给小明: {userMessage}");
        //    }
        //    else
        //    {
        //        Debug.LogWarning("输入框未绑定或内容为空！");
        //    }
        //}
        //private void OnGetAgentMessage(string agent, string ai_message)
        //{
        //    aiMessageText.text = $"{agent}: {ai_message}";
        //}


    }


}

