using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class SceneObjInfoRenderer
    {
        /// <summary>
        /// 渲染场景里所有场景对象信息
        /// </summary>
        /// <param name="sceneObjsInfo"></param>
        /// <param name="interactableObjInfo"></param>
        /// <returns></returns>
        public string Render(List<SceneObjInfoModel> sceneObjsInfo, SceneObjInfoModel interactableObjInfo)
        {
            string sceneObjsInfoDesc = "";

            if (sceneObjsInfo.Count > 0)
            {
                sceneObjsInfoDesc = "# 你的周围有:";
                // 1.遍历场景对象信息
                for (int i = 0; i < sceneObjsInfo.Count; i++)
                {
                    string sceneObjInfoDesc = $"\n{i}. {this.RenderSceneObj(sceneObjsInfo[i])}";
                    sceneObjsInfoDesc += sceneObjInfoDesc;
                }

                // 2.获取可交互对象信息
                string interactableObjDesc = "\n\n# 可选择交互:\n";
                if (interactableObjInfo != null)
                {
                    interactableObjDesc += $"{this.RenderSceneObj(interactableObjInfo)}";
                }
                else
                {
                    interactableObjDesc += "身边无可交互对象";
                }
                sceneObjsInfoDesc += interactableObjDesc;
            }

            return sceneObjsInfoDesc;
        }

        /// <summary>
        /// 渲染单个场景对象信息
        /// </summary>
        /// <param name="sceneObjInfo"></param>
        /// <returns></returns>
        public string RenderSceneObj(SceneObjInfoModel sceneObjInfo)
        {
            string sceneObjInfoDesc = "";
            if (sceneObjInfo != null)
            {
                string speed_x_str = sceneObjInfo.SpeedX <= 0.01f ? $"0m/s" : $"方向{sceneObjInfo.SpeedDirX} {sceneObjInfo.SpeedX}m/s";
                string speed_y_str = sceneObjInfo.SpeedY <= 0.01f ? $"0m/s" : $"方向{sceneObjInfo.SpeedDirY} {sceneObjInfo.SpeedDirY}m/s";
                sceneObjInfoDesc = $"{sceneObjInfo.Name}: {sceneObjInfo.Desc}\n" +
                    $"状态:{sceneObjInfo.State}\n" +
                    $"方向:{sceneObjInfo.Direction}\n距离:{sceneObjInfo.Distance}m\n" +
                    $"横向速度:{speed_x_str}\n纵向速度:{speed_y_str}";
            }
            else
            {
                sceneObjInfoDesc = "物体已消失";
            }
            return sceneObjInfoDesc;
        }
    }
}
