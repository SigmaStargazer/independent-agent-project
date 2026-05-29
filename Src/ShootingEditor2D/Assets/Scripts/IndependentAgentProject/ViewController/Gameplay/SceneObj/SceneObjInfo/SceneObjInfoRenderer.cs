using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
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
                string speedXStr = sceneObjInfo.SpeedX <= 0.01f ? $"0m/s" : $"方向{sceneObjInfo.SpeedDirX} {sceneObjInfo.SpeedX}m/s";
                string speedYStr = sceneObjInfo.SpeedY <= 0.01f ? $"0m/s" : $"方向{sceneObjInfo.SpeedDirY} {sceneObjInfo.SpeedDirY}m/s";
                string faceDirectionStr = !string.IsNullOrEmpty(sceneObjInfo.FaceDirection) ? faceDirectionStr = $"朝向:{sceneObjInfo.FaceDirection}\n" : "";
                // 方位
                string directionStr;
                if (sceneObjInfo.IsRangeDirection)
                {
                    directionStr =
                        $"方位: 从{sceneObjInfo.RangeLeftDirection}方向{sceneObjInfo.RangeLeftDistance}m " +
                        $"~ {sceneObjInfo.RangeRightDirection}方向{sceneObjInfo.RangeRightDistance}m";
                }
                else
                {
                    directionStr = $"方位:{sceneObjInfo.Direction}方向 {sceneObjInfo.Distance}m";
                }

                sceneObjInfoDesc = $"{sceneObjInfo.Name}: {sceneObjInfo.Desc}\n" +
                    $"状态:{sceneObjInfo.State}\n" +
                    $"{directionStr}\n" +
                    $"{faceDirectionStr}" +
                    $"横向速度:{speedXStr}\n纵向速度:{speedYStr}";
            }
            else
            {
                sceneObjInfoDesc = "物体已消失";
            }
            return sceneObjInfoDesc;
        }
    }
}
