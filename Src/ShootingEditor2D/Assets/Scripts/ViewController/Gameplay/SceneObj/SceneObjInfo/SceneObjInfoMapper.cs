using System.Collections.Generic;
using UnityEngine;

namespace ShootingEditor2D
{
    public class SceneObjInfoMapper
    {
        public (List<SceneObjInfoModel> sceneObjsInfo, SceneObjInfoModel targetObjInfo) GetSceneObjsInfo(GameObject agentGo, List<SceneObjBase> sceneObjs)
        {
            // 初始化返回值
            List<SceneObjInfoModel> sceneObjsInfo = new List<SceneObjInfoModel>();
            SceneObjInfoModel targetObjInfo = null;

            if (agentGo == null)
            {
                Debug.LogError("[SceneObjInfoMapper报错]agentGo == null！");
                return (sceneObjsInfo, targetObjInfo);
            }

            if (SceneObjManager.Instance == null)
            {
                Debug.LogError("[SceneObjInfoMapper报错]场景中未找到 SceneObjManager！");
                return (sceneObjsInfo, targetObjInfo);
            }

            float charaX = agentGo.transform.position.x;
            foreach (var sceneObj in sceneObjs)
            {
                SceneObjInfoModel sceneObjInfo = this.Map(sceneObj, agentGo);
                sceneObjsInfo.Add(sceneObjInfo);
            }

            // 获取逻辑中定义的可交互对象（最近且接触）
            SceneObjBase targetObj = SceneObjManager.Instance.GetNearestInteractableObj(agentGo);
            if (targetObj != null)
            {
                // 如果找到了符合条件的对象，将其信息包装进字典
                targetObjInfo = this.Map(targetObj, agentGo);
            }

            return (sceneObjsInfo, targetObjInfo);
        }
        private SceneObjInfoModel Map(SceneObjBase sceneObj, GameObject agentGo)
        {

            // 如果对象不存在或者被禁用，则返回空字典
            if (sceneObj == null || !sceneObj.gameObject.activeInHierarchy)
                return null;
            SceneObjInfoModel sceneObjInfo = new SceneObjInfoModel();
            float charaX = agentGo.transform.position.x;
            // name
            sceneObjInfo.Name = sceneObj.Name;
            // desc
            sceneObjInfo.Desc = sceneObj.Desc;
            // dirction
            float xDiff = sceneObj.transform.position.x - charaX;
            string direction = xDiff < 0 ? "left" : "right";
            sceneObjInfo.Direction = direction;
            // distance
            sceneObjInfo.Distance = Mathf.Abs(xDiff);

            Rigidbody2D rb = sceneObj.GetComponent<Rigidbody2D>();
            Vector2 velocity = rb != null ? rb.velocity : Vector2.zero;
            // speed_x
            string speedDirX = velocity.x > 0.01f ? "right" : (velocity.x < -0.01f ? "left" : "");
            sceneObjInfo.SpeedDirX = speedDirX;
            sceneObjInfo.SpeedX = Mathf.Abs(velocity.x);
            // speed_y
            string speedDirY = velocity.y > 0.01f ? "up" : (velocity.y < -0.01f ? "down" : "");
            sceneObjInfo.SpeedDirY = speedDirY;
            sceneObjInfo.SpeedY = Mathf.Abs(velocity.y);
            // state
            sceneObjInfo.State = sceneObj.GetStateName();

            // SceneObj为CharaBase时，增加面朝方向
            if (sceneObj is CharaBase chara)
            {
                sceneObjInfo.FaceDirection = chara.isRight ? "right" : "left";
            }

            return sceneObjInfo;
        }
    }
}