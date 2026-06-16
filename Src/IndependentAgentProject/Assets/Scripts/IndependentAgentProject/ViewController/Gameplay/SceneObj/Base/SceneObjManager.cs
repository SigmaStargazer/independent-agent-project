using FrameworkDesign;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace IndependentAgentProject
{
    public class SceneObjManager : MonoSingleton<SceneObjManager>
    {
        // 使用 List 维护场景中所有的场景物体
        private List<SceneObjBase> mSceneObjs = new List<SceneObjBase>();

        public static event Action<SceneObjBase> OnSceneObjCreated;

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
            mSceneObjs.Clear();
        }
        #region 注册与注销逻辑

        public void Register(SceneObjBase SceneObj)
        {
            if (SceneObj != null && !mSceneObjs.Contains(SceneObj))
            {
                mSceneObjs.Add(SceneObj); // 添加到场景物体列表
                OnSceneObjCreated?.Invoke(SceneObj); //通过事件通知，将场景物体添加到场景物体列表中
            }
        }

        public void UnRegister(SceneObjBase SceneObj)
        {
            if (SceneObj != null && mSceneObjs.Contains(SceneObj))
            {
                mSceneObjs.Remove(SceneObj);
            }
        }

        #endregion
        
        /// <summary>
        /// 获取场景中指定类型的场景物体列表
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public List<T> GetSceneObjsOfType<T>() where T : SceneObjBase
        {
            return mSceneObjs.OfType<T>().ToList();
        }

        /// <summary>
        /// 获取去掉指定物体的场景物体列表
        /// </summary>
        /// <param name="excludeTarget"></param>
        /// <returns></returns>
        public List<SceneObjBase> GetSceneObjsExcluding(GameObject excludeTarget)
        {
            if (excludeTarget == null)
                return new List<SceneObjBase>(mSceneObjs);

            return mSceneObjs
                .Where(obj => obj.gameObject != excludeTarget)
                .ToList();
        }

        #region 检索设备信息

        /// <summary>
        /// 获取与角色接触且距离最近的可交互物体
        /// </summary>
        public SceneObjBase GetNearestInteractableObj(GameObject chara)
        {
            if (chara == null) return null;

            // 获取角色的碰撞体，用于检测接触
            Collider2D charaCollider = chara.GetComponent<Collider2D>();
            if (charaCollider == null) return null;

            //DeviceBase[] devices = FindObjectsOfType<DeviceBase>();
            SceneObjBase target = null;
            float minDistance = float.MaxValue;

            // 找到最近且接触的设备
            foreach (var sceneObj in mSceneObjs)
            {
                if (sceneObj is IInteractable)
                {
                    IInteractable interactable = sceneObj as IInteractable;
                    //Collider2D objCollider = sceneObj.GetComponent<Collider2D>();
                    // 测试
                    //var colliderDist = new ColliderDistance2D();
                    //if (sceneObj.gameObject != chara)
                    //    colliderDist = objCollider.Distance(charaCollider);
                    //Debug.Log($"接触判断条件可视化: {sceneObj.Name}\n" +
                    //    $"sceneObj.gameObject != chara: {sceneObj.gameObject != chara}" +
                    //    $"interactable.IsInteractable: {interactable.IsInteractable}\n" +
                    //    $"objCollider != null: {objCollider != null}\n" +
                    //    $"objCollider.isTrigger: {objCollider.isTrigger}\n" +
                    //    $"charaCollider.Distance(objCollider).isOverlapped: {colliderDist}");
                    if (sceneObj.gameObject != chara 
                        && interactable.IsInteractable 
                        && sceneObj.IsCharacterInAnyZone(chara))
                    {
                        // 计算X轴距离（与GetDevicesInfo中的逻辑保持一致）
                        float dist = sceneObj.GetNearestZoneDistance(chara);

                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            target = sceneObj;
                        }
                    }
                }
            }
            return target;
        }
        #endregion

        #region Agent消息发送

        /// <summary>
        /// 向指定Agent发送消息
        /// </summary>
        public bool SendMessageToAgent(string agentName, string msg, bool forceInterrupt = false)
        {
            if (string.IsNullOrEmpty(agentName))
                return false;

            AIPlayer targetAgent = mSceneObjs.OfType<AIPlayer>().FirstOrDefault(agent => agent.Name == agentName);
            if (targetAgent == null)
            {
                Debug.LogWarning($"未找到Agent: {agentName}");
                return false;
            }

            targetAgent.SendMessageToAgent(msg,forceInterrupt);
            return true;
        }

        /// <summary>
        /// 向所有Agent广播消息
        /// </summary>
        public void BroadcastMessageToAgents(string msg, bool forceInterrupt = false)
        {
            List<AIPlayer> agents = mSceneObjs.OfType<AIPlayer>().ToList();
            foreach (AIPlayer agent in agents)
            {
                if (agent != null)
                {
                    agent.SendMessageToAgent(msg, forceInterrupt);
                }
            }

            Debug.Log($"已向 {agents.Count} 个Agent广播消息");
        }

        #endregion

        #region 交互逻辑

        public (bool success, string result) Interact(GameObject chara)
        {
            SceneObjBase target = GetNearestInteractableObj(chara);

            if (target != null && target is IInteractable)
            {
                string deviceName = target.Name;
                IInteractable interactable = target as IInteractable;
                (bool success, string result) = interactable.Interact(chara);
                return (success, $"{deviceName}:\n{result}");
            }
            else
            {
                return (false, "没有可交互对象");
                //Debug.Log($"没有可交互设备");
            }
        }

        public (bool success, string result) Select(GameObject chara, int selection)
        {
            SceneObjBase target = GetNearestInteractableObj(chara);

            if (target != null && target is IInteractable)
            {
                string deviceName = target.Name;
                IInteractable interactable = target as IInteractable;
                (bool success, string result) = interactable.Select(chara, selection);
                return (success, $"{deviceName}:\n{result}");
            }
            else
            {
                return (false, "没有可交互对象");
                //Debug.Log($"没有可交互设备");
            }
        }

        public (bool success, string result) TextInput(GameObject chara, string inputText)
        { 
            SceneObjBase target = GetNearestInteractableObj(chara);
            if (target != null && target is IInteractable)
            {
                string deviceName = target.Name;
                IInteractable interactable = target as IInteractable;
                (bool success, string result) = interactable.TextInput(chara, inputText);
                return (success, $"{deviceName}:\n{result}");
            }
            else
            {
                return (false, "没有可交互对象");
                //Debug.Log($"没有可交互设备");
            }
        }

        #endregion
    }
}
