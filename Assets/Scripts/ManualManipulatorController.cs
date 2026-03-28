using UnityEngine;

/// <summary>
/// Manual joint control via Inspector sliders and on-screen GUI.
/// Add this component to the scene instead of IKReceiver + SBCController
/// when you want to drive the arm by hand.
/// </summary>
public class ManualManipulatorController : MonoBehaviour
{
    public Manipulator manipulator;

    private float[] _angles;

    void Start()
    {
        var instances = manipulator.GetActuatorInstances();
        _angles = new float[instances.Count];
        for (int i = 0; i < _angles.Length; i++)
            _angles[i] = instances[i].GetPosition();
    }

    void Update()
    {
        var instances = manipulator.GetActuatorInstances();
        for (int i = 0; i < instances.Count && i < _angles.Length; i++)
            instances[i].SetPosition(_angles[i]);
    }

    void OnGUI()
    {
        if (_angles == null || _angles.Length == 0) return;

        float width = 400;
        float startY = 50;

        GUILayout.BeginArea(new Rect(10, startY, width, Screen.height - startY - 10));

        GUIStyle titleStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter
        };
        GUILayout.Label("Manual Control", titleStyle, GUILayout.Height(30));

        var instances = manipulator.GetActuatorInstances();
        for (int i = 0; i < instances.Count && i < _angles.Length; i++)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Joint {i}: {_angles[i]:F2}°", GUILayout.Height(20));

            float minAngle = -180f;
            float maxAngle =  180f;

            foreach (var cfg in manipulator.config)
            {
                if (cfg.type == Manipulator.ConfigType.Joint &&
                    cfg.joint != null &&
                    cfg.joint.GetActuatorInstance() == instances[i] &&
                    cfg.joint.axisConfig.hasLimits)
                {
                    minAngle = cfg.joint.axisConfig.minAngle;
                    maxAngle = cfg.joint.axisConfig.maxAngle;
                    break;
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{minAngle:F0}°", GUILayout.Width(50));
            _angles[i] = GUILayout.HorizontalSlider(_angles[i], minAngle, maxAngle);
            GUILayout.Label($"{maxAngle:F0}°", GUILayout.Width(50));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.Space(5);
        }

        GUILayout.EndArea();
    }
}
