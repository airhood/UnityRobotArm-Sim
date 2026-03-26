#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(Manipulator))]
public class ManipulatorEditor : Editor
{
    private ReorderableList reorderableList;
    private bool showSummary = false;

    private void OnEnable()
    {
        Manipulator manipulator = (Manipulator)target;
        
        reorderableList = new ReorderableList(
            manipulator.config,
            typeof(Manipulator.ManipulatorConfig),
            true,  // draggable
            true,  // displayHeader
            true,  // displayAddButton
            true   // displayRemoveButton
        );

        // Header
        reorderableList.drawHeaderCallback = (Rect rect) =>
        {
            EditorGUI.LabelField(rect, "Manipulator Configuration");
        };

        // Draw each element
        reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var config = manipulator.config[index];
            
            rect.y += 2;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = 2f;
            
            // Type dropdown
            Rect typeRect = new Rect(rect.x, rect.y, rect.width, lineHeight);
            config.type = (Manipulator.ConfigType)EditorGUI.EnumPopup(typeRect, "Type", config.type);
            
            rect.y += lineHeight + spacing;
            
            // Type-specific fields
            switch (config.type)
            {
                case Manipulator.ConfigType.Link:
                    Rect linkLengthRect = new Rect(rect.x, rect.y, rect.width, lineHeight);
                    Rect weightRect = new Rect(rect.x, rect.y + lineHeight + spacing, rect.width, lineHeight);

                    config.link ??= new Link();
                    config.link.linkLength = EditorGUI.FloatField(linkLengthRect, "Link Length", config.link.linkLength);
                    config.link.weight     = EditorGUI.FloatField(weightRect,     "Weight",      config.link.weight);
                    break;
                    
                case Manipulator.ConfigType.Joint:
                    Rect jointRect = new Rect(rect.x, rect.y, rect.width, lineHeight);
                    config.joint = (AngleJoint)EditorGUI.ObjectField(jointRect, "Angle Joint", config.joint, typeof(AngleJoint), true);
                    break;
                
                case Manipulator.ConfigType.FixedAngleJoint:
                    Rect axisRect  = new Rect(rect.x, rect.y, rect.width, lineHeight);
                    Rect angleRect = new Rect(rect.x, rect.y + lineHeight + spacing, rect.width, lineHeight);

                    config.fixedAngleJoint ??= new FixedAngleJoint();
                    config.fixedAngleJoint.axis  = (FixedAngleJoint.Axis)EditorGUI.EnumPopup(axisRect,  "Axis",  config.fixedAngleJoint.axis);
                    config.fixedAngleJoint.angle = EditorGUI.FloatField(angleRect, "Angle", config.fixedAngleJoint.angle);
                    break;
                    
                case Manipulator.ConfigType.EndEffector:
                    Rect effectorRect = new Rect(rect.x, rect.y, rect.width, lineHeight);
                    config.endEffector = (EndEffector)EditorGUI.ObjectField(effectorRect, "End Effector", config.endEffector, typeof(EndEffector), true);
                    break;
            }
        };

        // Element height
        reorderableList.elementHeightCallback = (int index) =>
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = 2f;
    
            var config = manipulator.config[index];
            
            int extraLines = (config.type == Manipulator.ConfigType.Link ||
                              config.type == Manipulator.ConfigType.FixedAngleJoint) ? 2 : 1;
    
            return lineHeight * (1 + extraLines) + spacing * 3 + 4;
        };

        // On add
        reorderableList.onAddCallback = (ReorderableList list) =>
        {
            manipulator.config.Add(new Manipulator.ManipulatorConfig());
            EditorUtility.SetDirty(manipulator);
        };

        // On remove
        reorderableList.onRemoveCallback = (ReorderableList list) =>
        {
            manipulator.config.RemoveAt(list.index);
            EditorUtility.SetDirty(manipulator);
        };
    }

    public override void OnInspectorGUI()
    {
        Manipulator manipulator = (Manipulator)target;
        serializedObject.Update();
        
        reorderableList.DoLayoutList();
        
        EditorGUILayout.Space();

        // Configuration Summary foldout
        showSummary = EditorGUILayout.Foldout(showSummary, "Configuration Summary", true);
        if (showSummary)
        {
            GUIStyle summaryStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 12,
                wordWrap = false
            };
            string summary = BuildSummary(manipulator.config);
            float height = summaryStyle.CalcHeight(new GUIContent(summary), EditorGUIUtility.currentViewWidth);
            EditorGUILayout.SelectableLabel(summary, summaryStyle, GUILayout.Height(height));
        }

        EditorGUILayout.Space();
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("IKReceiver"));
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("controlMode"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("endPoint"));
        
        if (manipulator.controlMode == Manipulator.ControlMode.Manual && Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Manual Joint Control", EditorStyles.boldLabel);
            
            var actuatorInstances = manipulator.GetActuatorInstances();
            
            for (int i = 0; i < actuatorInstances.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                float targetAngle = manipulator.GetManualAngle(i);
                float currentAngle = actuatorInstances[i].GetPosition();
                
                AngleJoint joint = null;
                foreach (var config in manipulator.config)
                {
                    if (config.type == Manipulator.ConfigType.Joint && 
                        config.joint != null && 
                        config.joint.GetActuatorInstance() == actuatorInstances[i])
                    {
                        joint = config.joint;
                        break;
                    }
                }
                
                float minAngle = -180f;
                float maxAngle = 180f;
                
                if (joint != null && joint.axisConfig.hasLimits)
                {
                    minAngle = joint.axisConfig.minAngle;
                    maxAngle = joint.axisConfig.maxAngle;
                }
                
                EditorGUILayout.LabelField($"Joint {i} - Target: {targetAngle:F2}° Current: {currentAngle:F2}°", EditorStyles.boldLabel);
                float newAngle = EditorGUILayout.Slider("Target Angle", targetAngle, minAngle, maxAngle);
                
                if (!Mathf.Approximately(newAngle, targetAngle))
                {
                    manipulator.SetManualAngle(i, newAngle);
                }
                
                EditorGUILayout.EndVertical();
            }
            
            if (Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
                Repaint();
            }
        }
        
        serializedObject.ApplyModifiedProperties();
        
        if (GUI.changed)
        {
            EditorUtility.SetDirty(target);
        }
    }

    private string BuildSummary(System.Collections.Generic.List<Manipulator.ManipulatorConfig> config)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Manipulator(");

        int jointIndex = 0;
        for (int i = 0; i < config.Count; i++)
        {
            var c = config[i];
            switch (c.type)
            {
                case Manipulator.ConfigType.Link:
                    sb.AppendLine($"  ({i}) Link(length={c.link?.linkLength:G}, weight={c.link?.weight:G})");
                    break;

                case Manipulator.ConfigType.Joint:
                    sb.AppendLine($"  ({i}) Joint[{jointIndex++}](name={c.joint?.name ?? "None"}, axis={c.joint?.selectedAxis})");
                    break;

                case Manipulator.ConfigType.FixedAngleJoint:
                    sb.AppendLine($"  ({i}) FixedAngleJoint(axis={c.fixedAngleJoint?.axis}, angle={c.fixedAngleJoint?.angle:F3}°)");
                    break;

                case Manipulator.ConfigType.EndEffector:
                    sb.AppendLine($"  ({i}) EndEffector(name={c.endEffector?.name ?? "None"})");
                    break;
            }
        }

        sb.Append(")");
        return sb.ToString();
    }
}
#endif