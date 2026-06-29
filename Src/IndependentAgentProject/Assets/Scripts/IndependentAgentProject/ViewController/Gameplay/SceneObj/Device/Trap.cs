using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 训练场陷阱。参考 Abyss 的实现，但用 PlayerBase.ReturnToCheckPointByHurt 取代 Die，
    /// 让训练循环可以重复进行而无需重新加载场景。
    /// v0.21.7-fix：改用受伤型版本，使 IInvulnerableState（如 Hidden / Dead）下传送被免疫。
    ///
    /// 使用方式：给本物体加 BoxCollider2D（IsTrigger=true）。
    /// </summary>
    public class Trap : SceneObjBase
    {
        public override string Name => string.IsNullOrEmpty(customName) ? "陷阱" : customName;
        public override string Desc => string.IsNullOrEmpty(customDesc)
            ? "触碰会被传送回最近的检查点。"
            : customDesc;

        [SerializeField] private string customName = "";
        [SerializeField, TextArea] private string customDesc = "";

        private void OnTriggerEnter2D(Collider2D collision)
        {
            PlayerBase player = collision.GetComponent<PlayerBase>();
            if (player != null)
            {
                player.ReturnToCheckPointByHurt(this.Name);
            }
        }
    }
}
