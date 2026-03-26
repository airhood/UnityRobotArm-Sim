using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

[CreateAssetMenu(fileName = "New Actuator", menuName = "Actuator")]
public class Actuator : ScriptableObject
{
    [System.Serializable]
    public class AngleRange
    {
        public bool hasLimits = false;
        public float minAngle = -90f;
        public float maxAngle = 90f;
    }

    [System.Serializable]
    public class PerformanceData
    {
        public float torque;
        public float speed;
        public float current;
        
        public PerformanceData(float t = 0f, float s = 0f, float c = 0f)
        {
            torque = t;
            speed = s;
            current = c;
        }
    }

    [Header("Basic Info")]
    public new string name;

    public string instanceClass;
    
    [Header("Specifications")]
    public float maxTorque;
    public float maxSpeed;
    public float maxCurrent;
    
    public float stallTorque;
    public float stallCurrent;
    
    [Header("Angle Limits")]
    public AngleRange angleRange = new AngleRange();

    [Header("Performance Curve")]
    public List<PerformanceData> performanceData = new List<PerformanceData>();

    public Type GetInstanceType()
    {
        if (String.IsNullOrEmpty(instanceClass))
        {
            Debug.LogError($"Instance class name for {name} is not set");
            return null;
        }

        Type type = Type.GetType(instanceClass);

        if (type == null)
        {
            Debug.LogError($"Instance class {instanceClass} for {name} is not found");
            return null;
        }

        if (!typeof(ActuatorInstance).IsAssignableFrom(type))
        {
            Debug.LogError($"Instance class {instanceClass} for {name} is not a Actuator");
            return null;
        }

        return type;
    }
    
    
    public float GetSpeed(float torque)
    {
        if (performanceData.Count == 0) return 0f;
        if (performanceData.Count == 1) return performanceData[0].speed;
        
        for (int i = 0; i < performanceData.Count - 1; i++)
        {
            if (torque >= performanceData[i].torque && torque <= performanceData[i + 1].torque)
            {
                float t = (torque - performanceData[i].torque) / (performanceData[i + 1].torque - performanceData[i].torque);
                return Mathf.Lerp(performanceData[i].speed, performanceData[i + 1].speed, t);
            }
        }
        
        return performanceData[performanceData.Count - 1].speed;
    }
    
    public float GetCurrent(float torque)
    {
        if (performanceData.Count == 0) return 0f;
        if (performanceData.Count == 1) return performanceData[0].current;
        
        for (int i = 0; i < performanceData.Count - 1; i++)
        {
            if (torque >= performanceData[i].torque && torque <= performanceData[i + 1].torque)
            {
                float t = (torque - performanceData[i].torque) / (performanceData[i + 1].torque - performanceData[i].torque);
                return Mathf.Lerp(performanceData[i].current, performanceData[i + 1].current, t);
            }
        }
        
        return performanceData[performanceData.Count - 1].current;
    }
}