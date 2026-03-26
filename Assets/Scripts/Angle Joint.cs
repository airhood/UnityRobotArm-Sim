using System;
using UnityEngine;

public class AngleJoint : MonoBehaviour, IManipulator
{
    public enum AxisType
    {
        X,
        Y,
        Z
    }
    
    [System.Serializable]
    public class AxisConfig
    {
        public bool isMovable = false;
        public bool hasLimits = false;
        public float minAngle = -90f;
        public float maxAngle = 90f;
        public float initialAngle = 0f;
        public float initialActuatorAngle = 0f;
    }
    
    [Header("Axis Selection")]
    public AxisType selectedAxis = AxisType.X;
    
    [Header("Axis Configuration")]
    public AxisConfig axisConfig = new AxisConfig();

    [Header("Actuator")]
    public Actuator actuator;
    
    public AxisConfig XAxis => selectedAxis == AxisType.X ? axisConfig : null;
    public AxisConfig YAxis => selectedAxis == AxisType.Y ? axisConfig : null;
    public AxisConfig ZAxis => selectedAxis == AxisType.Z ? axisConfig : null;
    
    private ActuatorInstance actuatorInstance;

    private float anglePosition;

    private float connectedWeight;

    void Awake()
    {
        Type actuatorInstanceType = actuator.GetInstanceType();

        if (actuatorInstanceType != null)
        {
            actuatorInstance = (ActuatorInstance)Activator.CreateInstance(
                actuatorInstanceType,
                actuator,
                this);
        }
        else
        {
            Debug.LogError("No actuator instance found.");
        }
    }
    
    void Start()
    {
        
    }

    void Update()
    {
        
    }

    public void Tick()
    {
        anglePosition = actuatorInstance.GetPosition();

        Quaternion rotation = Quaternion.identity;
        switch (selectedAxis)
        {
            case AxisType.X:
                rotation = Quaternion.AngleAxis(anglePosition, Vector3.right);
                break;
            case AxisType.Y:
                rotation = Quaternion.AngleAxis(anglePosition, Vector3.up);
                break;
            case AxisType.Z:
                rotation = Quaternion.AngleAxis(anglePosition, Vector3.forward);
                break;
        }
        transform.localRotation = rotation;
    }

    public float GetAnglePosition()
    {
        return anglePosition;
    }

    public ActuatorInstance GetActuatorInstance()
    {
        return actuatorInstance;
    }
}