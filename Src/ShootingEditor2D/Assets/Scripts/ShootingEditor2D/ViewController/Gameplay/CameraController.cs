using UnityEngine;
using UnityEngine.Timeline;

namespace ShootingEditor2D
{
    public class CameraController : MonoBehaviour
    {
        private Transform mPlayerTrans;

        private Vector3 mTargetPos;

        // 这里限制的范围代表地图边界
        private float mMinX = -5;
        private float mMaxX = 5;
        private float mMinY = -5;
        private float mMaxY = 5;
        //LateUpdate在一帧的最后进行计算
        private void LateUpdate()
        {
            if (!mPlayerTrans)
            {
                var playerGameObj = GameObject.FindWithTag("Player");
                if (playerGameObj)
                {
                    mPlayerTrans = playerGameObj.transform;
                }
                else
                {
                    // 退出Update方法
                    return;
                }
            }

            //var cameraPos = transform.position;
            //var playerPos = mPlayerTrans.position;

            //cameraPos = playerPos + new Vector3(3*Mathf.Sign(mPlayerTrans.localScale.x), 2, cameraPos.z - playerPos.z);
            //transform.position = cameraPos;

            //var isRight = Mathf.Sign(mPlayerTrans.localScale.x);
            var isRight = mPlayerTrans.GetComponent<Player>().isRight;
            var playerPos = mPlayerTrans.position;

            mTargetPos.x = playerPos.x + 3 * isRight;
            mTargetPos.y = playerPos.y + 2;
            mTargetPos.z = -10;

            var smoothSpeed = 5;
            // 增加一个平滑处理
            var position = transform.position;
            position = Vector3.Lerp(position,
                mTargetPos, smoothSpeed * Time.deltaTime);
            // 锁定在一个固定区域
            //public static float Clamp (float value, float min, float max);
            // 将值value限制在（min，max）内
            transform.position = new Vector3(
                Mathf.Clamp(position.x, mMinX, mMaxX),
                Mathf.Clamp(position.y, mMinY, mMaxY),
                position.z);
        }
    }
}

