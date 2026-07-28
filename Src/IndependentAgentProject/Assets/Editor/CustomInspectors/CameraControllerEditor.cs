using UnityEditor;
using UnityEngine;
using IndependentAgentProject;

namespace IndependentAgentProject.Editor
{
    [CustomEditor(typeof(CameraController))]
    public class CameraControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty m_BoundsEnabled;
        private SerializedProperty m_BoundsCenter;
        private SerializedProperty m_BoundsSize;

        private void OnSceneGUI()
        {
            serializedObject.Update();
            m_BoundsEnabled = serializedObject.FindProperty("boundsEnabled");
            m_BoundsCenter = serializedObject.FindProperty("boundsCenter");
            m_BoundsSize = serializedObject.FindProperty("boundsSize");

            if (m_BoundsEnabled == null || !m_BoundsEnabled.boolValue)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            Vector2 center = m_BoundsCenter.vector2Value;
            Vector2 size = m_BoundsSize.vector2Value;

            // 中心移动手柄
            EditorGUI.BeginChangeCheck();
            Vector3 centerHandlePos = Handles.PositionHandle(
                new Vector3(center.x, center.y, 0f),
                Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "移动相机范围中心");
                m_BoundsCenter.vector2Value = new Vector2(centerHandlePos.x, centerHandlePos.y);
            }

            // 四角缩放手柄
            Vector3[] corners = new Vector3[]
            {
                new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, 0f), // 左下
                new Vector3(center.x + size.x * 0.5f, center.y - size.y * 0.5f, 0f), // 右下
                new Vector3(center.x + size.x * 0.5f, center.y + size.y * 0.5f, 0f), // 右上
                new Vector3(center.x - size.x * 0.5f, center.y + size.y * 0.5f, 0f), // 左上
            };
            Vector3[] opposite = new Vector3[]
            {
                corners[2], // 左下 对 右上
                corners[3], // 右下 对 左上
                corners[0], // 右上 对 左下
                corners[1], // 左上 对 右下
            };

            for (int i = 0; i < 4; i++)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.FreeMoveHandle(
                    corners[i],
                    Quaternion.identity,
                    HandleUtility.GetHandleSize(corners[i]) * 0.1f,
                    Vector3.zero,
                    Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "调整相机范围大小");
                    Vector3 opp = opposite[i];
                    Vector3 min = Vector3.Min(newPos, opp);
                    Vector3 max = Vector3.Max(newPos, opp);
                    m_BoundsCenter.vector2Value = new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
                    m_BoundsSize.vector2Value = new Vector2(max.x - min.x, max.y - min.y);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
