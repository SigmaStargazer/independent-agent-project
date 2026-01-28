using FrameworkDesign;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class DeviceManager : MonoSingleton<DeviceManager>
    {
        // 使用 List 维护场景中所有的设备
        private List<DeviceBase> mDevices = new List<DeviceBase>();

        #region 注册与注销逻辑

        public void Register(DeviceBase device)
        {
            if (device != null && !mDevices.Contains(device))
            {
                mDevices.Add(device);
            }
        }

        public void UnRegister(DeviceBase device)
        {
            if (device != null && mDevices.Contains(device))
            {
                mDevices.Remove(device);
            }
        }

        #endregion

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        void OnDestroy()
        {
            mDevices.Clear();
        }

        #region 检索设备信息

        /// <summary>
        /// 获取与角色接触且距离最近的设备
        /// </summary>
        private DeviceBase GetInteractableDevice(GameObject chara)
        {
            if (chara == null) return null;

            // 获取角色的碰撞体，用于检测接触
            Collider2D charaCollider = chara.GetComponent<Collider2D>();
            if (charaCollider == null) return null;

            //DeviceBase[] devices = FindObjectsOfType<DeviceBase>();
            DeviceBase targetDevice = null;
            float minDistance = float.MaxValue;

            // 找到最近且接触的设备
            foreach (var device in mDevices)
            {
                Collider2D deviceCollider = device.GetComponent<Collider2D>();
                // 确保设备有Trigger类型的Collider且当前与角色接触
                if (device.IsInteractable && deviceCollider != null && deviceCollider.isTrigger && Physics2D.IsTouching(charaCollider, deviceCollider))
                {
                    // 计算X轴距离（与GetDevicesInfo中的逻辑保持一致）
                    float xDiff = device.transform.position.x - chara.transform.position.x;
                    float dist = Mathf.Abs(xDiff);

                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        targetDevice = device;
                    }
                }
            }
            return targetDevice;
        }

        /// <summary>
        /// 获取设备信息
        /// </summary>
        /// <param name="chara">Agent的Gameobject</param>
        /// <returns></returns>
        public (List<Dictionary<string, object>> devicesInfo, Dictionary<string, object> interactableDeviceInfo) GetDevicesInfo(GameObject chara)
        {
            // 初始化返回值
            var devicesInfo = new List<Dictionary<string, object>>();
            Dictionary<string, object> interactableDeviceInfo = new Dictionary<string, object>();

            if (chara == null)
            {
                return (devicesInfo, interactableDeviceInfo);
            }

            float charaX = chara.transform.position.x;
            //DeviceBase[] devices = FindObjectsOfType<DeviceBase>();
            foreach (var device in mDevices)
            {
                Dictionary<string, object> deviceInfo = DeviceBaseToDeviceInfo(device, chara);
                devicesInfo.Add(deviceInfo);
            }

            // 获取逻辑中定义的可交互设备（最近且接触）
            DeviceBase targetDevice = GetInteractableDevice(chara);
            if (targetDevice != null)
            {
                // 如果找到了符合条件的设备，将其信息包装进字典
                interactableDeviceInfo = DeviceBaseToDeviceInfo(targetDevice, chara);
            }

            return (devicesInfo, interactableDeviceInfo);
        }

        private Dictionary<string, object> DeviceBaseToDeviceInfo(DeviceBase device, GameObject chara)
        {
            float charaX = chara.transform.position.x;
            Dictionary<string, object> deviceInfo = new Dictionary<string, object>();

            // name
            deviceInfo.Add("name", device.Name);
            // desc
            deviceInfo.Add("desc", device.Desc);
            // dirction
            float xDiff = device.transform.position.x - charaX;
            string direction = xDiff < 0 ? "left" : "right";
            deviceInfo.Add("direction", direction);
            // distance
            deviceInfo.Add("distance", Mathf.Abs(xDiff));

            Rigidbody2D rb = device.GetComponent<Rigidbody2D>();
            Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;
            // speed_x
            string speedDirX = velocity.x > 0.01f ? "right" : (velocity.x < -0.01f ? "left" : "");
            deviceInfo.Add("speedDir_x", speedDirX);
            deviceInfo.Add("speed_x", Mathf.Abs(velocity.x));
            // speed_y
            string speedDirY = velocity.y > 0.01f ? "up" : (velocity.y < -0.01f ? "down" : "");
            deviceInfo.Add("speedDir_y", speedDirY);
            deviceInfo.Add("speed_y", Mathf.Abs(velocity.y));
            // state
            deviceInfo.Add("state", device.GetStateName());

            return deviceInfo;
        }

        #endregion

        #region 交互逻辑

        public string Interact(GameObject chara)
        {
            DeviceBase targetDevice = GetInteractableDevice(chara);

            if (targetDevice != null)
            {
                return targetDevice.Interact(chara);
            }
            else
            {
                return "没有可交互设备";
                //Debug.Log($"没有可交互设备");
            }
        }

        public string Select(GameObject chara, int selection)
        {
            DeviceBase targetDevice = GetInteractableDevice(chara);

            if (targetDevice != null)
            {
                return targetDevice.Select(chara, selection);
            }
            else
            {
                return "没有可交互设备";
                //Debug.Log($"没有可交互设备");
            }
        }

        public string TextInput(GameObject chara, string inputText)
        { 
            DeviceBase targetDevice = GetInteractableDevice(chara);
            if (targetDevice != null)
            {
                return targetDevice.TextInput(chara, inputText);
            }
            else
            {
                return "没有可交互设备";
                //Debug.Log($"没有可交互设备");
            }
        }

        #endregion
    }
}
