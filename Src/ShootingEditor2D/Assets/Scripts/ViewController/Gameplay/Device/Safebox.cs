using ShootingEditor2D;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Windows;

namespace ShootingEditor2D
{
    public class Safebox : DeviceBase
    {
        public string pwd;

        public override string Name => "保险箱";
        public override string Desc => "坚固的保险箱，上面似乎有密码锁";
        public override bool IsInteractable => true;

        protected override void Awake()
        {
            //添加状态
            RegisterState(new OpenState());
            RegisterState(new CloseState());
        }

        protected override void Start()
        {
            // 默认进入Close状态
            ChangeState("Close");
        }

        public override string Interact(GameObject chara)
        {
            switch (mCurState.Name)
            {
                case "Open":
                    return "保险箱已打开";
                case "Close":
                    if (pwd == null || !Regex.IsMatch(pwd, @"^\d{4}$"))
                        return PwdNotSettedOpen();
                    else
                        return "请输入4位数密码: ____";
                default:
                    return "";
            }
        }

        public override string TextInput(GameObject chara, string inputText)
        {
            switch (mCurState.Name)
            {
                case "Open":
                    return "设备未提供输入框";
                case "Close":
                    if (pwd == null || !Regex.IsMatch(pwd, @"^\d{4}$"))
                        return PwdNotSettedOpen();
                    else
                    {
                        if (Regex.IsMatch(inputText, @"^\d{4}$"))
                        {
                            if (inputText == pwd)
                            {
                                ChangeState("Open");
                                return "打开保险箱成功";
                            }
                            else
                            {
                                return "密码错误";
                            }
                        }
                        else
                        {
                            return "输入不是4位数字！";
                        }
                    }
                default:
                    return "";
            }
        }

        /// <summary>
        /// 密码未设置时，直接打开
        /// </summary>
        /// <returns></returns>
        private string PwdNotSettedOpen()
        {
            if (pwd == null)
            {
                Debug.Log("SafeBox未设置密码");
            }
            else if (!Regex.IsMatch(pwd, @"^\d{4}$"))
            {
                Debug.Log("SafeBox密码不是4位数字");
            }
            ChangeState("Open");
            return "打开保险箱成功";
        }

        public class OpenState : FSMStateBase
        {
            public override string Name => "Open";
        }

        public class CloseState : FSMStateBase
        {
            public override string Name => "Close";
        }
    }
}
