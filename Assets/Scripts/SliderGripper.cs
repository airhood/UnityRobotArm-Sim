using System;
using UnityEngine;

public class SliderGripper : EndEffector
{
    [Header("Crank Angle Joint")]
    public AngleJoint angleJoint;
    
    [Header("Connecting Rod")]
    public Transform connectingRod1;
    public Transform connectingRod2;

    [Header("Slider")]
    public Transform slider1;
    public Transform slider2;

    [Header("Figures")]
    public float crankRadius;
    public float connectingRodLength;

    [Header("Gripper Control")]
    public float openAngle = -90f;
    public float closeAngle = 0f;

    public void SetGripperOpen(bool open)
    {
        angleJoint.GetActuatorInstance().SetPosition(open ? openAngle : closeAngle);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Tick()
    {
        SyncAngles();
    }

    private void SyncAngles()
    {
        float crankAngle = angleJoint.GetAnglePosition();
        float connectingRodAngle = CalculateRodAngleRelToCrank(crankAngle);
        connectingRod1.localEulerAngles = new Vector3(connectingRodAngle, 0, 0);
        connectingRod2.localEulerAngles = new Vector3(connectingRodAngle, 0, 0);
        float sliderPosition = CalculateSliderPosition(crankAngle);
        Vector3 pos1 = slider1.localPosition;
        pos1.x = sliderPosition;
        slider1.localPosition = pos1;
        Vector3 pos2 = slider2.localPosition;
        slider2.localPosition = pos2;
        pos2.x = -sliderPosition;
    }

    private float CalculateRodAngleRelToCrank(float crankAngle)
    {
        return 180 - crankAngle - CalculateRodAngle(crankAngle);
    }
    
    private float CalculateRodAngle(float crankAngle)
    {
        crankAngle = NormalizeAngle(crankAngle);
        return Mathf.Asin((crankRadius * Mathf.Sin(crankAngle)) / connectingRodLength);
    }

    private float CalculateSliderPosition(float crankAngle)
    {
        return crankRadius * Mathf.Cos(crankAngle)
               + Mathf.Sqrt((connectingRodLength * connectingRodLength) - (crankRadius * crankRadius * Mathf.Sin(crankAngle) * Mathf.Sin(crankAngle)));
    }

    private float NormalizeAngle(float angle)
    {
        angle = Mathf.Repeat(Mathf.Abs(angle), 360f);
        if (angle > 180) angle = 360 - angle;
        return angle;
    }
}
