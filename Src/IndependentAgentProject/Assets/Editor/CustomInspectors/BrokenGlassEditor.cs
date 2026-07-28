using UnityEditor;
using UnityEngine;
using IndependentAgentProject;

namespace IndependentAgentProject.Editor
{
    [CustomEditor(typeof(BrokenGlass))]
    public class BrokenGlassEditor : UnityEditor.Editor
    {
        private SerializedProperty m_AttractRadius;

        private void OnEnable()
        {
            m_AttractRadius = serializedObject.FindProperty("mAttractRadius");
        }

        private void OnSceneGUI()
        {
            if (m_AttractRadius == null) return;
            serializedObject.Update();

            BrokenGlass t = (BrokenGlass)target;
            Vector3 center = t.transform.position;

            // 手柄固定白色，与既有绿色线框区分
            Color oldColor = Handles.color;
            Handles.color = Color.white;

            EditorGUI.BeginChangeCheck();
            float newRadius = Handles.RadiusHandle(Quaternion.identity, center, m_AttractRadius.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "调整碎玻璃响声半径");
                m_AttractRadius.floatValue = Mathf.Max(0f, newRadius);
            }

            Handles.color = oldColor;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
