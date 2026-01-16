using FrameworkDesign;
using Services;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace ShootingEditor2D
{
    public class Agent : ShootingEditor2DController
    {
        private Rigidbody2D mRigidbody2D;
        private Trigger2DCheck mGroundCheck;
        //private Gun mGun;
        public float isRight;

        public float moveSpeed = 5f;

        // 本帧是否按了跳
        private bool mJumpPressed;

        // 【新增】是否正在进行自动移动（用于屏蔽玩家输入）
        private bool mIsAutoMoving = false;

        private void Awake()
        {
            mRigidbody2D = GetComponent<Rigidbody2D>();
            mGroundCheck = transform.Find("GroundCheck").GetComponent<Trigger2DCheck>();
            //mGun = transform.Find("Gun").GetComponent<Gun>();
        }
        private void Start()
        {
            AgentService.Instance.OnMoveAgent = this.OnMoveAgent;
        }

        private void Update()
        {
            GetInput();
        }
        // 所有物理相关的逻辑放FixedUpdate（不受实际帧数影响，防穿）
        // 其他逻辑可以放Update
        private void FixedUpdate()
        {
            var rawInput = Input.GetAxis("Horizontal");
            float horizontalDirection = 0;
            if (Mathf.Abs(rawInput) > 0.01f)
            {
                horizontalDirection = Mathf.Sign(rawInput);
            }

            //var horizontalMovement = Input.GetAxis("Horizontal");
            isRight = Mathf.Sign(transform.localScale.x);

            TurnBack(horizontalDirection);
            MoveAndJump(horizontalDirection);
            //TurnBack(horizontalMovement);
            //MoveAndJump(horizontalMovement);

        }
        private void GetInput()
        {
            //if (Input.GetKeyDown(KeyCode.Space))
            //{
            //    mJumpPressed = true;
            //}
            ////if (Input.GetKeyDown(KeyCode.J))
            ////{
            ////    mGun.Shoot();
            ////}
            ////if (Input.GetKeyDown(KeyCode.R))
            ////{
            ////    mGun.Reload();
            ////}
            //if (Input.GetKeyDown(KeyCode.Q))
            //{
            //    this.SendCommand<ShiftGunCommand>();
            //}
        }

        private void TurnBack(float horizontalDirection)
        {
            if (horizontalDirection < 0 && transform.localScale.x > 0
                || horizontalDirection > 0 && transform.localScale.x < 0)
            {
                var localScale = transform.localScale;
                localScale.x = -localScale.x;
                transform.localScale = localScale;
            }
        }

        private void MoveAndJump(float horizontalDirection)
        {
            mRigidbody2D.velocity = new Vector2(horizontalDirection * moveSpeed, mRigidbody2D.velocity.y);

            var grounded = mGroundCheck.Triggered;

            if (mJumpPressed && grounded)
            {
                mRigidbody2D.velocity = new Vector2(mRigidbody2D.velocity.x, 5);
            }
            mJumpPressed = false;
        }

        public void MoveByDistance(bool moveRight, float distance)
        {
            // 停止之前的移动协程（防止多次调用冲突）
            StopAllCoroutines();
            StartCoroutine(MoveDistanceCoroutine(moveRight, distance));
        }

        private IEnumerator MoveDistanceCoroutine(bool moveRight, float distance)
        {
            mIsAutoMoving = true; // 锁定输入

            float startX = transform.position.x;
            float directionSign = moveRight ? 1f : -1f;
            float targetX = startX + (distance * directionSign);

            // 确保朝向正确
            TurnBack(directionSign);

            // 循环直到到达目标位置
            // 判断条件：如果是向右走，当前x小于目标x；如果是向左走，当前x大于目标x
            while ((moveRight && transform.position.x < targetX) ||
                   (!moveRight && transform.position.x > targetX))
            {
                // 保持物理移动速度
                mRigidbody2D.velocity = new Vector2(directionSign * moveSpeed, mRigidbody2D.velocity.y);

                // 等待下一次物理帧
                yield return new WaitForFixedUpdate();
            }

            // 到达目标，刹车
            mRigidbody2D.velocity = new Vector2(0, mRigidbody2D.velocity.y);

            mIsAutoMoving = false; // 恢复输入
        }

        private void OnMoveAgent(bool moveRight, float distance)
        {
            MoveByDistance(moveRight, distance);
        }
    }
}

