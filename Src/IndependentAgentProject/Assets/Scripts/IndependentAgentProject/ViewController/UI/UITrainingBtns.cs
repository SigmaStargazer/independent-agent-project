using Services;
using Sirenix.Utilities;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class UITrainingBtns : MonoBehaviour
    {
        public string agentName;
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        public void OnClickSaveSkills()
        {
            // 1. agent名为空
            if (agentName.IsNullOrWhitespace())
            {
                Debug.LogWarning("请输入技能保存的agent名");
                return;
            }
            // 2 将agent技能导出
            AgentService.Instance.SendAgentExportSkills(agentName);
        }
    }
}

