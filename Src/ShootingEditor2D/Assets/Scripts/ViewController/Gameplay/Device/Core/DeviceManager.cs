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

        public static event Action<DeviceBase> OnDeviceCreated;

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
        #region 注册与注销逻辑

        public void Register(DeviceBase device)
        {
            if (device != null && !mDevices.Contains(device))
            {
                mDevices.Add(device); // 添加到设备列表
                OnDeviceCreated?.Invoke(device); //通过事件通知，将设备添加到设备列表中
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

        public List<DeviceBase> GetDevices()
        {
            return mDevices;
        }

        #region 检索设备信息

        /// <summary>
        /// 获取与角色接触且距离最近的设备
        /// </summary>
        public DeviceBase GetInteractableDevice(GameObject chara)
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

        #endregion

        #region 交互逻辑

        public (bool success, string result) Interact(GameObject chara)
        {
            DeviceBase targetDevice = GetInteractableDevice(chara);

            if (targetDevice != null)
            {
                string deviceName = targetDevice.Name;
                (bool success, string result) = targetDevice.Interact(chara);
                return (success, $"{deviceName}:\n{result}");
            }
            else
            {
                return (false, "没有可交互设备");
                //Debug.Log($"没有可交互设备");
            }
        }

        public (bool success, string result) Select(GameObject chara, int selection)
        {
            DeviceBase targetDevice = GetInteractableDevice(chara);

            if (targetDevice != null)
            {
                string deviceName = targetDevice.Name;
                (bool success, string result) = targetDevice.Select(chara, selection);
                return (success, $"{deviceName}:\n{result}");
            }
            else
            {
                return (false, "没有可交互设备");
                //Debug.Log($"没有可交互设备");
            }
        }

        public (bool success, string result) TextInput(GameObject chara, string inputText)
        { 
            DeviceBase targetDevice = GetInteractableDevice(chara);
            if (targetDevice != null)
            {
                string deviceName = targetDevice.Name;
                (bool success, string result) = targetDevice.TextInput(chara, inputText);
                return (success, $"{deviceName}:\n{result}");
            }
            else
            {
                return (false, "没有可交互设备");
                //Debug.Log($"没有可交互设备");
            }
        }

        #endregion
    }
}
