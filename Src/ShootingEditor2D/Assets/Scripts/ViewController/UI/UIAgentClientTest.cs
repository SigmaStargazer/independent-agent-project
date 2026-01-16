using Services;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SkillBridge.Message;

public class UIAgentClientTest : MonoBehaviour
{
    public InputField inputField;
    public Text text;
    // Start is called before the first frame update
    void Start()
    {
        AgentService.Instance.OnGetAgentMessage = this.OnGetAgentMessage;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void OnClickSend()
    {
        // 创建Agent
        AgentService.Instance.SendAgentCreate("小明", "是一个帮助机器人");
        AgentService.Instance.SendAgentCreate("小红", "是用户的秘书");
        AgentService.Instance.SendSceneStart(1);
        AgentService.Instance.SendUserMessage("小明", "和小红说，让她闹个每天8点的起床铃，9点的上班铃声，然后每天闹铃响时让她直接通知我。");
    }

    public void OnClickSendText()
    {
        // 创建Agent
        AgentService.Instance.SendAgentCreate("小明", "是一个帮助机器人");
        AgentService.Instance.SendAgentCreate("小红", "是用户的秘书");
        AgentService.Instance.SendSceneStart(1);

        // 发送消息
        if (inputField != null && !string.IsNullOrEmpty(inputField.text))
        {
            // 获取输入框的内容
            string content = inputField.text;

            // 发送给小明
            AgentService.Instance.SendUserMessage("小明", content);

            // 清空输入框
            inputField.text = "";

            Debug.Log($"已发送消息给小明: {content}");
        }
        else
        {
            Debug.LogWarning("输入框未绑定或内容为空！");
        }
    }
    private void OnGetAgentMessage(string agent, string ai_message)
    {
        text.text = $"{agent}: {ai_message}";
    }


}


