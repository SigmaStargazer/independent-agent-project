using UnityEngine;

namespace IndependentAgentProject
{
    public class CameraController : MonoBehaviour
    {
        [SerializeField]
        private GameObject player;
        [SerializeField]
        private float smoothTime = 0.2f;

        [Header("移动范围限制")]
        [SerializeField]
        private bool boundsEnabled = false;
        [SerializeField]
        private Vector2 boundsCenter = Vector2.zero;
        [SerializeField]
        private Vector2 boundsSize = new Vector2(20f, 10f);
        [SerializeField]
        private Color boundsGizmoColor = new Color(1f, 0.5f, 0f, 0.8f);

        private HumanPlayer mPlayer;
        private Vector3 mTargetPos;
        private Vector3 velocity;
        private Camera mCamera;
        // 运行时锁定的相机深度（Start 时缓存），不再硬编码 -10
        private float mBaseDepth;
        // 矩形比视口还小时的 Warning 去重标记
        private bool mBoundsTooSmallWarned;

        private void Start()
        {
            if (player != null)
            {
                mPlayer = player.GetComponent<HumanPlayer>();
            }
            mCamera = GetComponent<Camera>();
            mBaseDepth = transform.position.z;
        }

        private void LateUpdate()
        {
            if (player == null || mPlayer == null)
            {
                return;
            }

            var isRight = mPlayer.IsRight;
            var playerPos = player.transform.position;

            mTargetPos.x = playerPos.x + (isRight ? 3 : -3);
            mTargetPos.y = playerPos.y + 2;
            // 深度锁定为启动时缓存的值，便于在 Scene 中调整相机 z 后保持
            mTargetPos.z = mBaseDepth;

            // 平滑处理
            var position = transform.position;
            position = Vector3.SmoothDamp(
                position,
                mTargetPos,
                ref velocity,
                smoothTime);

            // 范围限制：使相机视口四边不超出配置矩形（正交相机）
            if (boundsEnabled && mCamera != null && boundsSize.x > 0f && boundsSize.y > 0f)
            {
                float halfH = mCamera.orthographicSize;
                float halfW = halfH * mCamera.aspect;
                float minX = boundsCenter.x - boundsSize.x * 0.5f + halfW;
                float maxX = boundsCenter.x + boundsSize.x * 0.5f - halfW;
                float minY = boundsCenter.y - boundsSize.y * 0.5f + halfH;
                float maxY = boundsCenter.y + boundsSize.y * 0.5f - halfH;

                // 矩形比视口还小（无解）时，该轴退化为夹到中心，并打一次 Warning
                if (minX > maxX)
                {
                    minX = maxX = boundsCenter.x;
                    WarnBoundsTooSmall('X');
                }
                if (minY > maxY)
                {
                    minY = maxY = boundsCenter.y;
                    WarnBoundsTooSmall('Y');
                }

                position.x = Mathf.Clamp(position.x, minX, maxX);
                position.y = Mathf.Clamp(position.y, minY, maxY);
            }

            // z 不受 clamp 影响，保持为 mBaseDepth（SmoothDamp 目标恒定，已收敛）
            position.z = mBaseDepth;
            transform.position = position;
        }

        private void WarnBoundsTooSmall(char axis)
        {
            if (mBoundsTooSmallWarned) return;
            mBoundsTooSmallWarned = true;
            Debug.LogWarning(
                $"[CameraController] 配置的移动范围矩形在 {axis} 轴上比相机视口还小，" +
                "该轴已退化为夹到矩形中心。请调大 boundsSize 或缩小 orthographicSize。");
        }

        private void OnDrawGizmos()
        {
            if (!boundsEnabled) return;
            Gizmos.color = boundsGizmoColor;
            Gizmos.DrawWireCube(boundsCenter, boundsSize);
        }
    }
}
