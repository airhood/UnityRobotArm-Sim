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

    public static float tickInterval = 0.02f;

    private List<ActuatorInstance> actuatorInstances = new List<ActuatorInstance>();
    private List<AngleJoint>       angleJoints       = new List<AngleJoint>();
    private List<EndEffector>      endEffectors      = new List<EndEffector>();

    public Transform endPoint;
    public Transform gripperPivot;
    
    void Start()
    {
        if (SimCore.Instance == null)
        { Debug.LogError("[Manipulator] SimCore not found."); return; }

        ConfigureTickInstances();
        SimCore.Instance.Register(Tick, priority: 10);
    }

    void OnDestroy()
    {
        SimCore.Instance?.Unregister(Tick);
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
            else if (config[i].type == ConfigType.EndEffector)
            {
                if (config[i].endEffector != null)
                    endEffectors.Add(config[i].endEffector);
            }
        }
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
        for (int i = 0; i < endEffectors.Count; i++)
        {
            endEffectors[i].Tick();
        }
    }
    
    public List<ActuatorInstance> GetActuatorInstances()
    {
        return actuatorInstances;
    }

    // ── Gripper API ───────────────────────────────────────────────────────

    public void SetGripperOpen(bool open)
    {
        if (endEffectors.Count == 0)
        { Debug.LogError("[Manipulator] SetGripperOpen: no EndEffector configured"); return; }
        foreach (var ee in endEffectors) ee.SetOpen(open);
    }

    public bool IsGripperSettled()
    {
        if (endEffectors.Count == 0)
        { Debug.LogError("[Manipulator] IsGripperSettled: no EndEffector configured"); return true; }
        foreach (var ee in endEffectors)
            if (!ee.IsSettled()) return false;
        return true;
    }

    // ── State API ─────────────────────────────────────────────────────────

    public float[] GetCurrentJointAngles()
    {
        float[] angles = new float[actuatorInstances.Count];
        for (int i = 0; i < actuatorInstances.Count; i++)
            angles[i] = actuatorInstances[i].GetPosition();
        return angles;
    }
}