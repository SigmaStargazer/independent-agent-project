using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 视野 / 攻击子物体的 Trigger 事件转发器。
    /// 由父 EnemyBase 在 Awake 中动态 AddComponent 并 Init(this, kind)。
    /// 显式按 kind 把 Trigger 事件分发到父级方法，避免父级用子物体 name 字符串判别。
    /// </summary>
    public enum EnemyZoneKind { Vision, Attack }

    public class EnemyZoneForwarder : MonoBehaviour
    {
        private EnemyBase mOwner;
        private EnemyZoneKind mKind;

        public void Init(EnemyBase owner, EnemyZoneKind kind)
        {
            mOwner = owner;
            mKind = kind;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (mOwner == null) return;
            switch (mKind)
            {
                case EnemyZoneKind.Vision: mOwner.OnVisionEnter(other); break;
                case EnemyZoneKind.Attack: mOwner.OnAttackEnter(other); break;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (mOwner == null) return;
            if (mKind == EnemyZoneKind.Vision) mOwner.OnVisionExit(other);
        }
    }
}
