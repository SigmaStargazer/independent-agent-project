using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 交互区域组件，挂载在 SceneObjBase 子对象上
    /// 支持多区域
    /// </summary>
    public class InteractionZone : MonoBehaviour
    {
        [Tooltip("区域标签，如 front/back，留空则为默认区域")]
        public string ZoneTag = "";

        // 当前在区域内的角色碰撞体数量
        private int mOverlapCount = 0;
        public LayerMask TargetLayers;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (IsInLayerMask(other.gameObject, TargetLayers))
                mOverlapCount++;
        }
        private void OnTriggerExit2D(Collider2D other)
        {
            if (IsInLayerMask(other.gameObject, TargetLayers))
                mOverlapCount = Mathf.Max(0, mOverlapCount - 1);
        }

        private bool IsInLayerMask(GameObject obj, LayerMask layerMask)
        {
            var objLayerMask = 1 << obj.layer;
            return (layerMask.value & objLayerMask) > 0;
        }
        /// <summary>
        /// 指定角色是否在交互区域内
        /// <param name="chara">角色的GameObject</param>
        /// <returns>bool，该角色是否在该交互区域内</returns>
        /// </summary>
        public bool ContainsCharacter(GameObject chara)
        {
            if (chara == null) return false;
            var col = chara.GetComponent<Collider2D>();
            if (col == null) return false;
            var selfCol = GetComponent<Collider2D>();
            if (selfCol == null) return false;
            return selfCol.Distance(col).isOverlapped;
        }

        /// <summary>
        /// 获取角色与交互区域之间的距离
        /// </summary>
        /// <param name="chara">角色的GameObject</param>
        /// <return>float，角色与交互区域的距离</return>
        /// <returns></returns>
        public float DistanceTo(GameObject chara)
        {
            var selfCol = GetComponent<Collider2D>();
            var charaCol = chara?.GetComponent<Collider2D>();
            if (selfCol == null || charaCol == null)
                return float.MaxValue;
            return Vector2.Distance(selfCol.bounds.center, charaCol.bounds.center);
        }
    }
}
