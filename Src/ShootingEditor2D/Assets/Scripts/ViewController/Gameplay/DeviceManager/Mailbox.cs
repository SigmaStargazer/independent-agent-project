using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mailbox : DeviceBase
{
    // Start is called before the first frame update
    void Start()
    {
        deviceName = "信箱";
        deviceDesc = "里面可能存着信件。";
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public override string Interact(GameObject chara)
    {
        return "查阅信件：\nTo 小明：\n  记得提醒用户，他的备用钥匙在花盆下面";
        //Agent agent = chara.GetComponent<Agent>();
        //if (agent)
        //{
        //    //agent.SendMessageToAgent("查阅信件：\nTo 小明：\n  记得提醒用户，他的备用钥匙在花盆下面");
        //}
    }
}
