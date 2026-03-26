#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Actuator))]
public class ActuatorEditor : Editor
{
    private bool showSpeedGraph = true;
    private bool showCurrentGraph = true;
    
    public override void OnInspectorGUI()
    {
        Actuator actuator = (Actuator)target;
        
        // Basic Info
        EditorGUILayout.LabelField("Basic Info");
        actuator.name = EditorGUILayout.TextField("Name", actuator.name);
        actuator.instanceClass = EditorGUILayout.TextField("Instance Class", actuator.instanceClass);
        
        // Specifications
        EditorGUILayout.LabelField("Specifications", EditorStyles.boldLabel);
        actuator.maxTorque = EditorGUILayout.FloatField("Max Torque (Nm)", actuator.maxTorque);
        actuator.maxSpeed = EditorGUILayout.FloatField("Max Speed (RPM)", actuator.maxSpeed);
        actuator.maxCurrent = EditorGUILayout.FloatField("Max Current (A)", actuator.maxCurrent);
        actuator.stallTorque = EditorGUILayout.FloatField("Stall Torque (Nm)", actuator.stallTorque);
        actuator.stallCurrent = EditorGUILayout.FloatField("Stall Current (A)", actuator.stallCurrent);
        
        EditorGUILayout.Space(10);
        
        // Angle Limits
        EditorGUILayout.LabelField("Angle Limits", EditorStyles.boldLabel);
        actuator.angleRange.hasLimits = EditorGUILayout.Toggle("Has Limits", actuator.angleRange.hasLimits);
        
        if (actuator.angleRange.hasLimits)
        {
            EditorGUI.indentLevel++;
            actuator.angleRange.minAngle = EditorGUILayout.FloatField("Min Angle (deg)", actuator.angleRange.minAngle);
            actuator.angleRange.maxAngle = EditorGUILayout.FloatField("Max Angle (deg)", actuator.angleRange.maxAngle);
            
            if (actuator.angleRange.minAngle > actuator.angleRange.maxAngle)
            {
                actuator.angleRange.maxAngle = actuator.angleRange.minAngle;
            }
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space(10);
        
        // Performance data list
        SerializedProperty performanceDataProp = serializedObject.FindProperty("performanceData");
        EditorGUILayout.PropertyField(performanceDataProp, new GUIContent("Data Points"), true);
        
        EditorGUILayout.Space(10);
        
        // Graphs
        if (actuator.performanceData != null && actuator.performanceData.Count >= 2)
        {
            showSpeedGraph = EditorGUILayout.Foldout(showSpeedGraph, "Torque-Speed Graph", true);
            if (showSpeedGraph)
            {
                DrawGraph(actuator, GraphType.Speed);
            }
            
            EditorGUILayout.Space(10);
            
            showCurrentGraph = EditorGUILayout.Foldout(showCurrentGraph, "Torque-Current Graph", true);
            if (showCurrentGraph)
            {
                DrawGraph(actuator, GraphType.Current);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Add at least 2 data points to see graphs", MessageType.Info);
        }
        
        serializedObject.ApplyModifiedProperties();
        
        if (GUI.changed)
        {
            EditorUtility.SetDirty(actuator);
        }
    }
    
    private enum GraphType { Speed, Current }
    
    private void DrawGraph(Actuator actuator, GraphType graphType)
    {
        Rect graphRect = GUILayoutUtility.GetRect(10, 200);
        graphRect = EditorGUI.IndentedRect(graphRect);
        
        float padding = 40f;
        Rect drawRect = new Rect(
            graphRect.x + padding,
            graphRect.y + padding / 2,
            graphRect.width - padding - 20f,
            graphRect.height - padding
        );
        
        // Background
        EditorGUI.DrawRect(graphRect, new Color(0.15f, 0.15f, 0.15f));
        EditorGUI.DrawRect(drawRect, new Color(0.2f, 0.2f, 0.2f));
        
        // Grid
        Handles.color = new Color(0.3f, 0.3f, 0.3f);
        for (int i = 0; i <= 5; i++)
        {
            float y = drawRect.y + drawRect.height * i / 5f;
            Handles.DrawLine(new Vector3(drawRect.x, y), new Vector3(drawRect.xMax, y));
            
            float x = drawRect.x + drawRect.width * i / 5f;
            Handles.DrawLine(new Vector3(x, drawRect.y), new Vector3(x, drawRect.yMax));
        }
        
        // Find max values
        float maxTorque = actuator.maxTorque;
        float maxValue = graphType == GraphType.Speed ? actuator.maxSpeed : actuator.maxCurrent;
        
        // Draw lines and points
        Vector3[] linePoints = new Vector3[actuator.performanceData.Count];
        
        for (int i = 0; i < actuator.performanceData.Count; i++)
        {
            var data = actuator.performanceData[i];
            float value = graphType == GraphType.Speed ? data.speed : data.current;
            
            float x = drawRect.x + (data.torque / maxTorque) * drawRect.width;
            float y = drawRect.yMax - (value / maxValue) * drawRect.height;
            
            linePoints[i] = new Vector3(x, y, 0);
        }
        
        // Draw line
        Handles.color = graphType == GraphType.Speed ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.3f, 0.6f, 1f);
        Handles.DrawAAPolyLine(3f, linePoints);
        
        // Draw points
        Handles.color = Color.white;
        foreach (var point in linePoints)
        {
            Handles.DrawSolidDisc(point, Vector3.forward, 4f);
        }
        
        // Labels
        GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
        labelStyle.normal.textColor = Color.white;
        
        // X-axis labels
        GUI.Label(new Rect(drawRect.x - 5, drawRect.yMax + 5, 50, 20), "0", labelStyle);
        GUI.Label(new Rect(drawRect.xMax - 30, drawRect.yMax + 5, 100, 20), $"{maxTorque:F1}", labelStyle);
        GUI.Label(new Rect(drawRect.x + drawRect.width / 2 - 40, drawRect.yMax + 5, 100, 20), "Torque (Nm)", labelStyle);
        
        // Y-axis labels
        string unit = graphType == GraphType.Speed ? "rad/s" : "A";
        GUI.Label(new Rect(drawRect.x - 10, drawRect.yMax - 10, 50, 20), "0", labelStyle);
        GUI.Label(new Rect(drawRect.x - 35, drawRect.y - 10, 100, 20), $"{maxValue:F1}", labelStyle);
        
        // Y-axis title (rotated)
        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(-90, new Vector2(10, drawRect.y + drawRect.height / 2));
        string yLabel = graphType == GraphType.Speed ? "Speed (rad/s)" : "Current (A)";
        GUI.Label(new Rect(10, drawRect.y + drawRect.height / 2 - 30, 100, 20), yLabel, labelStyle);
        GUI.matrix = matrix;
    }
}
#endif