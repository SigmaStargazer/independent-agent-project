using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 训练场检查点。继承 SceneObjBase，玩家触碰时调用 PlayerBase.UpdateCheckPoint
    /// 把自己登记为最新重生点；触发陷阱时由 PlayerBase.ReturnToCheckPoint 把玩家
    /// 传送回 GetRespawnPosition()（即挂载的 respawnAnchor 位置，未挂则用自身 transform）。
    ///
    /// 使用方式：
    ///   1. 把 CheckPoint Prefab 拖进训练场景。
    ///   2. 在 Prefab 下创建一个空子物体，命名如 RespawnAnchor，把它放在略高于地面的位置；
    ///      把这个子物体拖到本组件 Inspector 的 respawnAnchor 字段，避免重生后角色卡进地里。
    ///   3. 给本物体加 BoxCollider2D（IsTrigger=true）作为触发器。
    /// </summary>
    public class CheckPoint : SceneObjBase
    {
        public override string Name => "检查点";
        public override string Desc => "训练场的安全点。触碰即记录为重生位置。";

        [SerializeField, Tooltip("挂一个略高出地面的子物体作为重生锚点；未设置则用 CheckPoint 自身位置")]
        private Transform respawnAnchor;

        public Vector3 GetRespawnPosition()
        {
            return respawnAnchor != null ? respawnAnchor.position : transform.position;
        }

        private void OnTriggerEnter2D(Collider2D collision)
        {
            PlayerBase player = collision.GetComponent<PlayerBase>();
            if (player != null)
            {
                player.UpdateCheckPoint(this);
            }
        }
    }
}
