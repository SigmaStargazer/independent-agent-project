using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Timeline;

namespace IndependentAgentProject
{
    public class CameraController : MonoBehaviour
    {
        [SerializeField]
        private GameObject player;
        [SerializeField]
        private float smoothTime = 0.2f;

        private HumanPlayer mPlayer;
        private Vector3 mTargetPos;
        private Vector3 velocity;

        //// 这里限制的范围代表地图边界
        //private float mMinX = -5;
        //private float mMaxX = 5;
        //private float mMinY = -5;
        //private float mMaxY = 5;
        //LateUpdate在一帧的最后进行计算

        private void Start()
        {
            if (player != null)
            {
                mPlayer = player.GetComponent<HumanPlayer>();
            }
        }
        private void LateUpdate()
        {
            if (player == null)
            {
                //var playerGameObj = GameObject.FindWithTag("Player");
                //if (playerGameObj)
                //{
                //    mPlayerTrans = playerGameObj.transform;
                //}
                //else
                //{
                //    // 退出Update方法
                //    return;
                //}
                return;
            }

            //var cameraPos = transform.position;
            //var playerPos = mPlayerTrans.position;

            //cameraPos = playerPos + new Vector3(3*Mathf.Sign(mPlayerTrans.localScale.x), 2, cameraPos.z - playerPos.z);
            //transform.position = cameraPos;

            //var isRight = Mathf.Sign(mPlayerTrans.localScale.x);
            var isRight = mPlayer.IsRight; ;
            var playerPos = player.transform.position;

            mTargetPos.x = playerPos.x + (isRight ? 3 : -3);
            mTargetPos.y = playerPos.y + 2;
            //mTargetPos.z = -10;

            // 增加一个平滑处理
            var position = transform.position;
            position = Vector3.SmoothDamp(
                position,
                mTargetPos,
                ref velocity,
                smoothTime);
            // 锁定在一个固定区域
            //public static float Clamp (float value, float min, float max);
            //// 将值value限制在（min，max）内
            //transform.position = new Vector3(
            //    Mathf.Clamp(position.x, mMinX, mMaxX),
            //    Mathf.Clamp(position.y, mMinY, mMaxY),
            //    position.z);
            transform.position = position;
        }
    }
}

