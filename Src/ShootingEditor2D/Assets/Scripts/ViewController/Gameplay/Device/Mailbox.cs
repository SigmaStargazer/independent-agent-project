using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class Mailbox : DeviceBase
    {
        public string pwd = "4753";
        public override string Name => "信箱";
        public override string Desc => "里面可能存着信件。";

        public override bool IsInteractable => true;

        public override string Interact(GameObject chara)
        {
            return "查阅信箱：共有3封信件。你可以选择查阅：" +
                "  1. [2015.1.1]来自小磊的信件" +
                "  2. [2015.9.1]来自小落的信件" +
                "  3. [2015,12,31]查阅来自小红信件";
        }

        public override string Select(GameObject chara, int selection)
        {
            switch (selection)
            {
                case 1:
                    return $"查阅来自小磊信件：\nTo 小明：\n  欢迎参加我们的测试！";
                case 2:
                    return $"查阅来自小落信件：\nTo 小明：\n  用户的备用钥匙在花盆下面";
                case 3:
                    return $"查阅来自小红信件：\nTo 小明：\n  保险箱的密码是{pwd}";
                default:
                    return "选项错误！请选择正确的选项";
            }
        }
    }

}
