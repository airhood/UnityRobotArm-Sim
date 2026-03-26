#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AngleJoint))]
public class AngleJointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AngleJoint joint = (AngleJoint)target;

        EditorGUILayout.LabelField("Axis Selection", EditorStyles.boldLabel);
        joint.selectedAxis = (AngleJoint.AxisType)EditorGUILayout.EnumPopup("Rotation Axis", joint.selectedAxis);
        
        EditorGUILayout.Space();

        string axisLabel = joint.selectedAxis.ToString() + " Axis Configuration";
        DrawAxisConfig(axisLabel, joint.axisConfig);
        
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Actuator", EditorStyles.boldLabel);
        joint.actuator = (Actuator)EditorGUILayout.ObjectField("Actuator", joint.actuator, typeof(Actuator), false);

        if (GUI.changed)
        {
            EditorUtility.SetDirty(joint);
        }
    }

    private void DrawAxisConfig(string label, AngleJoint.AxisConfig axis)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        
        axis.isMovable = EditorGUILayout.Toggle("Movable", axis.isMovable);
        
        if (axis.isMovable)
        {
            EditorGUI.indentLevel++;
            axis.hasLimits = EditorGUILayout.Toggle("Has Limits", axis.hasLimits);
            
            if (axis.hasLimits)
            {
                EditorGUI.indentLevel++;
                axis.minAngle = EditorGUILayout.FloatField("Min Angle", axis.minAngle);
                axis.maxAngle = EditorGUILayout.FloatField("Max Angle", axis.maxAngle);
                axis.initialAngle = EditorGUILayout.FloatField("Initial Angle", axis.initialAngle);
                axis.initialActuatorAngle = EditorGUILayout.FloatField("Initial Actuator Angle", axis.initialActuatorAngle);
                
                if (axis.minAngle > axis.maxAngle)
                {
                    axis.maxAngle = axis.minAngle;
                }
                
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.EndVertical();
    }
}
#endif