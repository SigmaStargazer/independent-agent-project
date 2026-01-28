using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class Telephone : DeviceBase
    {
        public override string Name => "电话";
        public override string Desc => "一台老式有线电话。";

        public override bool IsInteractable => false;

        public override string Interact(GameObject chara)
        {
            return "";
        }

        public override string Select(GameObject chara, int selection)
        {
            return "";
        }

        public override string TextInput(GameObject chara, string inputText)
        {
            return "";
        }
    }

}
