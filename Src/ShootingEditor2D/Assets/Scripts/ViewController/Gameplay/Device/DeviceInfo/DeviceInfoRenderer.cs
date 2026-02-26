using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class DeviceInfoRenderer
    {
        public string Render(List<DeviceInfoModel> devicesInfo, DeviceInfoModel interactableDeviceInfo)
        {
            string devicesInfoDesc = "";

            if (devicesInfo.Count > 0)
            {
                devicesInfoDesc = "# 你的周围有:";
                //int deviceId = 0;
                //// 1.遍历设备信息
                //foreach (var deviceInfo in devicesInfo)
                //{
                //    string deviceInfoDesc = $"\n{deviceId}. {this.RenderDevice(deviceInfo)}";

                //    devicesInfoDesc += deviceInfoDesc;
                //    deviceId++;
                //}
                // 1.遍历设备信息
                for (int i = 0; i < devicesInfo.Count; i++)
                {
                    string deviceInfoDesc = $"\n{i}. {this.RenderDevice(devicesInfo[i])}";
                    devicesInfoDesc += deviceInfoDesc;
                }

                // 2.获取可交互设备信息
                string interactableDevicDesc = "\n\n# 可选择交互:\n";
                if (interactableDeviceInfo != null)
                {
                    interactableDevicDesc += $"{this.RenderDevice(interactableDeviceInfo)}";
                }
                else
                {
                    interactableDevicDesc += "身边无可交互对象";
                }
                devicesInfoDesc += interactableDevicDesc;
            }

            return devicesInfoDesc;
        }

        public string RenderDevice(DeviceInfoModel deviceInfo)
        {
            string deviceInfoDesc = "";
            if (deviceInfo != null)
            {
                string speed_x_str = deviceInfo.SpeedX <= 0.01f ? $"0m/s" : $"方向{deviceInfo.SpeedDirX} {deviceInfo.SpeedX}m/s";
                string speed_y_str = deviceInfo.SpeedY <= 0.01f ? $"0m/s" : $"方向{deviceInfo.SpeedDirY} {deviceInfo.SpeedDirY}m/s";
                deviceInfoDesc = $"{deviceInfo.Name}: {deviceInfo.Desc}\n" +
                    $"状态:{deviceInfo.State}\n" +
                    $"方向:{deviceInfo.Direction}\n距离:{deviceInfo.Distance}m\n" +
                    $"横向速度:{speed_x_str}\n纵向速度:{speed_y_str}";
            }
            else
            {
                deviceInfoDesc = "物体已消失";
            }
            return deviceInfoDesc;
        }
    }
}
