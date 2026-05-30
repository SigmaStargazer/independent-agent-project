using System.Collections.Generic;
using UnityEngine;

namespace IndependentAgentProject
{
    public class MovingPlatformAuto : DeviceBase
    {
        public override string Name => "自动移动的平台";
        public override string Desc => "似乎会沿着某种路径往复运动。";
        public override bool IsInteractable => false;

        [Header("路径点")]
        [SerializeField]
        private List<Transform> mWayPoints = new();
        [SerializeField]
        private float mMoveSpeed = 2f;
        [SerializeField]
        private float mWaitTime = 1f;

        private int mCurrentIndex = 0;
        private float mWaitTimer = 0f;
        private Transform mTargetPoint;

        protected override void Start()
        {
            base.Start();
            if (mWayPoints.Count <= 1)
                return;

            mCurrentIndex = 0;
            SetNextTarget();
        }

        public override void OnIdleUpdate()
        {
            if (mWayPoints.Count <= 1)
                return;

            // Idle计时
            mWaitTimer += Time.deltaTime;
            if (mWaitTimer >= mWaitTime)
            {
                mWaitTimer = 0;
                SetNextTarget();
            }
        }

        public override void OnMoveFixedUpdate()
        {
            if (mTargetPoint == null)
                return;

            transform.position = Vector3.MoveTowards(
                transform.position,
                mTargetPoint.position,
                mMoveSpeed * Time.fixedDeltaTime
            );
            // 停止逻辑
            if (Vector3.Distance(transform.position, mTargetPoint.position) < 0.02f)
            {
                transform.position = mTargetPoint.position;
                ChangeState("Idle");
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
        private void SetNextTarget()
        {
            if (mWayPoints.Count <= 1)
                return;

            mCurrentIndex = (mCurrentIndex + 1) % mWayPoints.Count;
            mTargetPoint = mWayPoints[mCurrentIndex];
            ChangeState("Move");
        }
    }
}