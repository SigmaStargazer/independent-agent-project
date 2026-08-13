using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 下一关入口门
    /// 当所有玩家都进入交互区域后，允许进入下一关
    /// </summary>
    public class NextMapDoor : DeviceBase
    {
        [Header("下一关配置")]
        [Tooltip("Build Settings中的Scene名字")]
        public string NextScene;

        [Tooltip("下一关中文名")]
        public string NextMapName;

        #region 基础信息
        public override string Name => "门";
        public override string Desc
        {
            get
            {
                // 门封闭
                if (string.IsNullOrEmpty(NextScene))
                {
                    return "一扇封闭着的门";
                }
                // 未知区域
                if (string.IsNullOrEmpty(NextMapName))
                {
                    return "通向未知区域的门。当所有玩家都来到这时，可进入";
                }
                // 已知区域
                return $"通向{NextMapName}的门。当所有玩家都来到这时，可进入";
            }
        }
        public override bool IsInteractable => !string.IsNullOrEmpty(NextScene);
        #endregion

        #region 交互逻辑
        public override (bool success, string result, InteractAnimTag animTag) Interact(GameObject chara)
        {
            // 门封闭
            if (string.IsNullOrEmpty(NextScene))
            {
                return (false, "门似乎封闭着", InteractAnimTag.None);
            }

            // 人没到齐
            if (!AreAllPlayersInsideDoor())
            {
                int insideCount = GetPlayersInsideDoorCount();
                int totalCount = GetAllPlayers().Count;
                return (false, $"还有玩家未到达门前（{insideCount}/{totalCount}）", InteractAnimTag.None);
            }

            // 进入下一关
            GameFlowManager.Instance.NextMap(NextScene).Forget(Debug.LogException);
            return (true, $"正在前往{GetTargetMapDisplayName()}", InteractAnimTag.None);
        }
        #endregion

        #region 条件判断
        /// <summary>
        /// 所有玩家是否都在门区域内
        /// </summary>
        private bool AreAllPlayersInsideDoor()
        {
            List<PlayerBase> players = GetAllPlayers();

            if (players.Count == 0)
                return false;

            return players.All(player =>
                player != null &&
                IsCharacterInAnyZone(player.gameObject));
        }

        /// <summary>
        /// 当前在门区域内的玩家数量
        /// </summary>
        private int GetPlayersInsideDoorCount()
        {
            return GetAllPlayers().Count(player => player != null && IsCharacterInAnyZone(player.gameObject));
        }

        /// <summary>
        /// 获取场景内所有玩家
        /// </summary>
        private List<PlayerBase> GetAllPlayers()
        {
            return SceneObjManager.Instance.GetSceneObjsOfType<PlayerBase>();
        }

        /// <summary>
        /// 获取地图显示名
        /// </summary>
        private string GetTargetMapDisplayName()
        {
            return string.IsNullOrEmpty(NextMapName) ? "未知区域" : NextMapName;
        }
        #endregion
    }
}