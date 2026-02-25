using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class DeviceInfoMapper
    {
        public (List<DeviceInfoModel> devicesInfo, DeviceInfoModel interactableDeviceInfo) GetDevicesInfo(GameObject agentGo, List<DeviceBase> devices)
        {
            // 初始化返回值
            List<DeviceInfoModel> devicesInfo = new List<DeviceInfoModel>();
            DeviceInfoModel interactableDeviceInfo = null;

            if (agentGo == null)
            {
                Debug.LogError("[DeviceInfoMapper报错]agentGo == null！");
                return (devicesInfo, interactableDeviceInfo);
            }

            if (DeviceManager.Instance == null)
            {
                Debug.LogError("[DeviceInfoMapper报错]场景中未找到 DeviceManager！");
                return (devicesInfo, interactableDeviceInfo);
            }

            float charaX = agentGo.transform.position.x;
            //DeviceBase[] devices = FindObjectsOfType<DeviceBase>();
            foreach (var device in devices)
            {
                DeviceInfoModel deviceInfo = this.Map(device, agentGo);
                devicesInfo.Add(deviceInfo);
            }

            // 获取逻辑中定义的可交互设备（最近且接触）
            DeviceBase targetDevice = DeviceManager.Instance.GetInteractableDevice(agentGo);
            if (targetDevice != null)
            {
                // 如果找到了符合条件的设备，将其信息包装进字典
                interactableDeviceInfo = this.Map(targetDevice, agentGo);
            }

            return (devicesInfo, interactableDeviceInfo);
        }
        private DeviceInfoModel Map(DeviceBase device, GameObject agentGo)
        {

            // 如果设备不存在或者被禁用，则返回空字典
            if (device == null || !device.gameObject.activeInHierarchy)
                return null;
            DeviceInfoModel deviceInfo = new DeviceInfoModel();
            float charaX = agentGo.transform.position.x;
            // name
            deviceInfo.Name = device.Name;
            // desc
            deviceInfo.Desc = device.Desc;
            // dirction
            float xDiff = device.transform.position.x - charaX;
            string direction = xDiff < 0 ? "left" : "right";
            deviceInfo.Direction = direction;
            // distance
            deviceInfo.Distance = Mathf.Abs(xDiff);

            Rigidbody2D rb = device.GetComponent<Rigidbody2D>();
            Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;
            // speed_x
            string speedDirX = velocity.x > 0.01f ? "right" : (velocity.x < -0.01f ? "left" : "");
            deviceInfo.SpeedDirX = speedDirX;
            deviceInfo.SpeedX = Mathf.Abs(velocity.x);
            // speed_y
            string speedDirY = velocity.y > 0.01f ? "up" : (velocity.y < -0.01f ? "down" : "");
            deviceInfo.SpeedDirY = speedDirY;
            deviceInfo.SpeedY = Mathf.Abs(velocity.y);
            // state
            deviceInfo.State = device.GetStateName();

            return deviceInfo;
        }
    }
}