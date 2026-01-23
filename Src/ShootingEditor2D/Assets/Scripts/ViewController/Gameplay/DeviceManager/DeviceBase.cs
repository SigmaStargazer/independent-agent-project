using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public abstract class DeviceBase : MonoBehaviour
    {
        public string deviceName { get; protected set; }
        public string deviceDesc { get; protected set; }

        public abstract string Interact(GameObject chara);
    }

}
