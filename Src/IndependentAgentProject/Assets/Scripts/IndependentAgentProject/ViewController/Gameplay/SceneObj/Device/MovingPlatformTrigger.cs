using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class MovingPlatformTrigger : DeviceBase, ITriggerable
    {
        public override string Name => "移动平台";
        public override string Desc => "一个悬空的平台。不知道是否可以移动……";
        public override bool IsInteractable => false;

        [Header("路径点")]
        [SerializeField]
        private List<Transform> mWayPoints = new();
        [SerializeField]
        private float mMoveSpeed = 2f;

        // 当前所在路径点索引
        // -1表示开场不在任何路径点
        private int mCurrentIndex = -1;
        // 当前目标路径点
        private int mTargetIndex = -1;
        private Transform mTargetPoint;

        protected override void Start()
        {
            base.Start();

            mWayPoints.RemoveAll(x => x == null);

            if (mWayPoints.Count == 0)
                return;

            // 判断开场是否已经在某个路径点上
            for (int i = 0; i < mWayPoints.Count; i++)
            {
                if (Vector3.Distance(transform.position, mWayPoints[i].position) < 0.05f)
                {
                    mCurrentIndex = i;
                    break;
                }
            }
        }

        public override void OnMoveFixedUpdate()
        {
            if (mTargetPoint == null)
                return;

            float step = mMoveSpeed * Time.fixedDeltaTime;

            float remainDistance =Vector3.Distance(
                transform.position,
                mTargetPoint.position);

            // 移动完成
            if (remainDistance <= step)
            {
                transform.position = mTargetPoint.position;
                mCurrentIndex = mTargetIndex;
                mTargetIndex = -1;
                mTargetPoint = null;
                ChangeState("Idle");
            }
            else
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    mTargetPoint.position,
                    step);
            }
        }
        private void OnCollisionEnter2D(Collision2D collision)
        {
            collision.transform.SetParent(transform);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            collision.transform.SetParent(null);
        }
        public bool CanTrigger()
        {
            return StateName == "Idle";
        }

        public void Trigger()
        {
            if (mWayPoints.Count == 0)
                return;

            if (!CanTrigger())
                return;

            // 设置目标点的索引
            // 开场不在任何路径点
            if (mCurrentIndex == -1)
            {
                mTargetIndex = 0;
            }
            // mCurrentIndex + 1
            else
            {
                mTargetIndex = (mCurrentIndex + 1) % mWayPoints.Count;
            }
            mTargetPoint = mWayPoints[mTargetIndex];
            ChangeState("Move");
        }
    }
}