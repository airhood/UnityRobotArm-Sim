using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Manipulator : MonoBehaviour
{
    public enum ConfigType
    {
        Link,
        Joint,
        FixedAngleJoint,
        EndEffector
    }

    public enum ControlMode
    {
        IKReceiver,
        Manual
    }
    
    [System.Serializable]
    public class ManipulatorConfig
    {
        public ConfigType type;

        [Header("Link Settings")]
        public Link link;
        
        [Header("Joint Settings")]
        public AngleJoint joint;

        [Header("Fixed Joint Settings")]
        public FixedAngleJoint fixedAngleJoint;
        
        [Header("End Effector Settings")]
        public EndEffector endEffector;
    }
    
    public List<ManipulatorConfig> config = new List<ManipulatorConfig>();

    public IKReceiver IKReceiver;
    
    public ControlMode controlMode = ControlMode.IKReceiver;

    public static float tickInterval = 0.02f;
    
    private List<ActuatorInstance> actuatorInstances = new List<ActuatorInstance>();
    private List<AngleJoint> angleJoints = new List<AngleJoint>();

    private float[] manualAngles;
    
    private ControlMode lastControlMode;

    public Transform endPoint;
    
    void Start()
    {
        controlMode = ManipulatorModeSetting.controlMode;
        ConfigureTickInstances();
        
        if (controlMode == ControlMode.Manual)
        {
            InitializeManualMode();
        }
        else
        {
            IKReceiver.StartReceiver();
        }
        
        ConfigureDynamixelXM430W350T();
    }

    [Header("Joint Profile")]
    public int  profileVelocity     = 100;
    public int  profileAcceleration = 7;

    [Header("Joint Synchronization")]
    [Tooltip("모든 관절이 동시에 목표에 도달하도록 profileVelocity를 비례 조정")]
    public bool  syncJoints       = true;
    [Tooltip("이 각도(도) 이상 변화 시 동기화 재계산")]
    public float syncTriggerDelta = 2f;

    void ConfigureDynamixelXM430W350T()
    {
        foreach (var actuatorInstance in actuatorInstances)
        {
            if (actuatorInstance is IActuatorProfile<DynamixelProfile> p)
            {
                var config = p.GetProfile();
                config.profileVelocity     = profileVelocity;
                config.profileAcceleration = profileAcceleration;
                p.SetProfile(config);
            }
        }
    }

    private void ConfigureTickInstances()
    {
        for (int i = 0; i < config.Count; i++)
        {
            if (config[i].type == ConfigType.Joint)
            {
                if (config[i].joint == null)
                {
                    Debug.LogError("[Manipulator] Null AngleJoint configuration error!");
                    continue;
                }
                actuatorInstances.Add(config[i].joint.GetActuatorInstance());
                angleJoints.Add(config[i].joint);
            }
        }
    }
    
    private void InitializeManualMode()
    {
        if (actuatorInstances.Count == 0)
        {
            Debug.LogError("[Manipulator] No actuator instances!");
            return;
        }
        
        manualAngles = new float[actuatorInstances.Count];
        for (int i = 0; i < manualAngles.Length; i++)
        {
            manualAngles[i] = actuatorInstances[i].GetPosition();
        }
        Debug.Log($"[Manipulator] Manual Mode initialized with {manualAngles.Length} joints");
    }

    void Update()
    {
        if (controlMode != lastControlMode)
        {
            if (controlMode == ControlMode.Manual)
            {
                InitializeManualMode();
            }
            lastControlMode = controlMode;
        }
        
        if (controlMode == ControlMode.Manual && manualAngles != null)
        {
            for (int i = 0; i < actuatorInstances.Count && i < manualAngles.Length; i++)
            {
                actuatorInstances[i].SetPosition(manualAngles[i]);
            }
        }
    }

    void FixedUpdate()
    {
        Tick(Time.fixedDeltaTime);
    }

    private void Tick(float dt)
    {
        for (int i = 0; i < actuatorInstances.Count; i++)
        {
            actuatorInstances[i].Tick(dt);
        }
        for (int i = 0; i < angleJoints.Count; i++)
        {
            angleJoints[i].Tick();
        }
    }
    
    public void SetManualAngle(int index, float angle)
    {
        if (manualAngles != null && index >= 0 && index < manualAngles.Length)
        {
            manualAngles[index] = angle;
        }
    }
    
    public float GetManualAngle(int index)
    {
        if (manualAngles != null && index >= 0 && index < manualAngles.Length)
        {
            return manualAngles[index];
        }
        return 0f;
    }
    
    void OnGUI()
    {
        if (controlMode != ControlMode.Manual) return;
    
        if (manualAngles == null || manualAngles.Length == 0) return;
    
        float width = 400;
        float startY = 50;
    
        GUILayout.BeginArea(new Rect(10, startY, width, Screen.height - startY - 10));
    
        GUIStyle titleStyle = new GUIStyle(GUI.skin.box);
        titleStyle.fontSize = 16;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label("Manual Control Mode", titleStyle, GUILayout.Height(30));
    
        for (int i = 0; i < actuatorInstances.Count && i < manualAngles.Length; i++)
        {
            GUILayout.BeginVertical(GUI.skin.box);
        
            GUILayout.Label($"Joint {i}: {manualAngles[i]:F2}°", GUILayout.Height(20));
        
            float minAngle = -180f;
            float maxAngle = 180f;
        
            if (angleJoints[i].axisConfig.hasLimits)
            {
                minAngle = angleJoints[i].axisConfig.minAngle;
                maxAngle = angleJoints[i].axisConfig.maxAngle;
            }
        
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{minAngle:F0}°", GUILayout.Width(50));
            manualAngles[i] = GUILayout.HorizontalSlider(manualAngles[i], minAngle, maxAngle);
            GUILayout.Label($"{maxAngle:F0}°", GUILayout.Width(50));
            GUILayout.EndHorizontal();
        
            GUILayout.EndVertical();
            GUILayout.Space(5);
        }
    
        GUILayout.EndArea();
    }

    public List<ActuatorInstance> GetActuatorInstances()
    {
        return actuatorInstances;
    }
}