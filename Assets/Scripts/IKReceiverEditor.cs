#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(IKReceiver))]
public class IKReceiverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        IKReceiver receiver = (IKReceiver)target;

        if (receiver.target == null || receiver.endPoint == null) return;

        Vector3 t = receiver.target.position;
        Vector3 e = receiver.endPoint.position;
        Vector3 diff = e - t;
        float distMm = diff.magnitude * 1000f;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("IK Debug", EditorStyles.boldLabel);

        // 박스 스타일
        GUIStyle box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
        EditorGUILayout.BeginVertical(box);

        GUIStyle label = new GUIStyle(EditorStyles.label) { richText = true };

        EditorGUILayout.LabelField($"<b>Target  </b>  x: {t.x:F4}   y: {t.y:F4}   z: {t.z:F4}", label);
        EditorGUILayout.LabelField($"<b>EndPoint</b>  x: {e.x:F4}   y: {e.y:F4}   z: {e.z:F4}", label);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"<b>Diff    </b>  x: {diff.x:F4}   y: {diff.y:F4}   z: {diff.z:F4}", label);

        Color prevColor = GUI.contentColor;
        GUI.contentColor = distMm < 5f ? Color.green : distMm < 20f ? Color.yellow : Color.red;
        EditorGUILayout.LabelField($"<b>Distance</b>  {distMm:F1} mm", label);
        GUI.contentColor = prevColor;

        EditorGUILayout.EndVertical();

        if (Application.isPlaying) Repaint();
    }
}
#endif