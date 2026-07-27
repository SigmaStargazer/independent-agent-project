using FrameworkDesign;
using UnityEngine;

namespace IndependentAgentProject
{
    /// <summary>
    /// 碎玻璃装置：SceneObj 进入 Trigger 后广播 <see cref="EnemyAnomalyEvent"/>，
    /// 供 EnemyBase 等 AI 单位感知；不使用 Physics2D 主动查询。
    ///
    /// <para>发送方无差别广播（不做距离过滤）；距离过滤在订阅方的回调里完成。</para>
    /// <para>冷却期内可被继续踩踏但不会重复触发广播，避免玩家在 Trigger 上来回抖动导致刷屏。</para>
    /// <para>可被交互 / 点击均为 false（纯环境装置）。</para>
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class BrokenGlass : DeviceBase
    {
        public override string Name => "碎玻璃";
        public override string Desc => "地上散落的碎玻璃，踩到会发出声响。";
        public override bool IsInteractable => false;
        public override bool IsClickable => false;

        [Header("异常源配置")]
        [SerializeField][Tooltip("声音传播半径，作为 EnemyAnomalyEvent.Radius 上送。红色 Gizmos 可视化。")]
        private float mAttractRadius = 5f;

        [SerializeField][Tooltip("同一块碎玻璃两次广播的最小间隔（秒）。冷却期内 Trigger 进入不再广播。")]
        private float mCooldownSeconds = 1.5f;

        private float mCooldownEndTime = 0f;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (Time.time < mCooldownEndTime) return;
            // 只接受非 Trigger 的物理身体"踩"到玻璃。EnemyBase 的 VisionZone / AttackZone /
            // BackstabZone 等子 Trigger 会随 EnemyBase 移动扫过玻璃，若不过滤会误触发广播，
            // 导致附近其他 EnemyBase 被"路过者的视野"莫名警觉。
            if (other.isTrigger) return;

            SceneObjBase sceneObj = other.GetComponentInParent<SceneObjBase>();
            if (sceneObj == null) return;

            mCooldownEndTime = Time.time + mCooldownSeconds;
            this.GetArchitecture().SendEvent(new EnemyAnomalyEvent
            {
                SourcePos = transform.position,
                Radius = mAttractRadius,
                Triggerer = sceneObj,
                SourceObj = this,
            });
        }

        private void OnDrawGizmos()
        {
            //var box = GetComponent<BoxCollider2D>();
            //if (box != null)
            //{
            //    Gizmos.color = Color.yellow;
            //    Vector3 center = transform.TransformPoint(box.offset);
            //    Vector3 size = Vector3.Scale((Vector3)box.size, transform.lossyScale);
            //    Gizmos.DrawWireCube(center, size);
            //}

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, mAttractRadius);
        }
    }
}
