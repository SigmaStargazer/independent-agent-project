using FrameworkDesign;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class DeviceManager : MonoSingleton<DeviceManager>
    {
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        public List<Dictionary<string, object>> GetDevicesInfo(GameObject chara)
        {
            var devicesInfo = new List<Dictionary<string, object>>();

            if (chara == null)
            {
                return devicesInfo;
            }

            float charaX = chara.transform.position.x;

            DeviceBase[] devices = FindObjectsOfType<DeviceBase>();
            foreach (var device in devices)
            {
                Dictionary<string, object> deviceInfo = new Dictionary<string, object>();

                // name
                deviceInfo.Add("name", device.deviceName);
                // desc
                deviceInfo.Add("desc", device.deviceDesc);
                // dirction
                float xDiff = device.transform.position.x - charaX;
                string direction = xDiff < 0 ? "left" : "right";
                deviceInfo.Add("direction", direction);
                // distance
                deviceInfo.Add("distance", Mathf.Abs(xDiff));

                devicesInfo.Add(deviceInfo);
            }
            return devicesInfo;
        }
    }
}

