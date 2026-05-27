using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class Merchant : CharaBase
    {
        public override string Name => "商人";
        public override string Desc => "可以在这里购买商品\n" +
            "(偷偷告诉你，你也可以在他身后窃取财物哦！)";

        public override (bool success, string result) Interact(GameObject chara)
        {
            string zone = GetActiveZoneTag(chara);
            return zone switch
            {
                "front" => DoTrade(chara),
                "back" => DoSteal(chara),
                _ => (false, "无法交互")
            };
        }
        public override (bool success, string result) Select(GameObject chara, int selection)
        {
            string zone = GetActiveZoneTag(chara);
            switch (zone)
            {
                case "front":
                    {
                        return selection switch
                        {
                            1 => (true, "你购买了一个生命药水"),
                            2 => (true, "你购买了一个单手剑"),
                            3 => (true, "你购买了一个护甲"),
                            _ => (false, "选项错误！请选择正确的选项")
                        };
                    }
                default:
                    return (false, "选项错误！请选择正确的选项");
            }
        }
        private (bool success, string result) DoTrade(GameObject chara)
        {
            return (
                true,
                "你可以选择购买：" +
                "  1. 生命药水: 5金币" +
                "  2. 单手剑: 200金币" +
                "  3. 护甲: 100金币"
                );
        }
        private (bool success, string result) DoSteal(GameObject chara)
        {
            return (true, "你获得了10金币");
        }


    }
}

