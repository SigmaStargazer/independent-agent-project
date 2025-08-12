using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using FrameworkDesign;

namespace ShootingEditor2D
{
    public class Player : ShootingEditor2DController
    {
        private Rigidbody2D mRigidbody2D;
        private Trigger2DCheck mGroundCheck;
        private Gun mGun;
        public float isRight;

        // 本帧是否按了跳
        private bool mJumpPressed;
        private void Awake()
        {
            mRigidbody2D = GetComponent<Rigidbody2D>();
            mGroundCheck = transform.Find("GroundCheck").GetComponent<Trigger2DCheck>();
            mGun = transform.Find("Gun").GetComponent<Gun>();
        }
        private void Update()
        {
            GetInput();
        }
        // 所有物理相关的逻辑放FixedUpdate（不受实际帧数影响，防穿）
        // 其他逻辑可以放Update
        private void FixedUpdate()
        {
            var horizontalMovement = Input.GetAxis("Horizontal");
            isRight = Mathf.Sign(transform.localScale.x);
            TurnBack(horizontalMovement);
            Jump(horizontalMovement);

        }
        private void GetInput()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                mJumpPressed = true;
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                mGun.Shoot();
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                mGun.Reload();
            }
            if (Input.GetKeyDown(KeyCode.Q))
            {
                this.SendCommand<ShiftGunCommand>();
            }
        }

        private void TurnBack(float horizontalMovement)
        {
            if (horizontalMovement < 0 && transform.localScale.x > 0
                || horizontalMovement > 0 && transform.localScale.x < 0)
            {
                var localScale = transform.localScale;
                localScale.x = -localScale.x;
                transform.localScale = localScale;
            }
        }

        private void Jump(float horizontalMovement)
        {
            mRigidbody2D.velocity = new Vector2(horizontalMovement * 5, mRigidbody2D.velocity.y);

            var grounded = mGroundCheck.Triggered;

            if (mJumpPressed && grounded)
            {

                mRigidbody2D.velocity = new Vector2(mRigidbody2D.velocity.x, 5);
            }
            mJumpPressed = false;
        }
    }
}

