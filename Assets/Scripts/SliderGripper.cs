using System;
using UnityEngine;

public class SliderGripper : EndEffector
{
    [Header("Crank Angle Joint")]
    public AngleJoint angleJoint;
    
    [Header("Connecting Rod")]
    public Transform connectingRodL;
    public Transform connectingRodR;

    [Header("Slider")]
    [Tooltip("열릴 때 -z 방향으로 이동하는 슬라이더 (Left)")]
    public Transform sliderL;
    [Tooltip("열릴 때 +z 방향으로 이동하는 슬라이더 (Right)")]
    public Transform sliderR;

    [Header("Figures")]
    public float crankRadius;
    public float connectingRodLength;

    [Header("Gripper Control")]
    [Tooltip("0° = sliders 최대 벌어짐(open), -90° = sliders 최소(closed). 반대로 동작하면 Inspector에서 값 스왑하거나 우클릭 → Reset Gripper Angles 사용")]
    public float openAngle  = 0f;
    public float closeAngle = -90f;

    [Header("Actuator Profile Override")]
    [Tooltip("true면 Manipulator가 적용한 profileAcceleration을 아래 값으로 덮어씀")]
    public bool overrideAcceleration      = true;
    public int  overrideAccelerationValue = 0;

    private bool _profileApplied = false;

    public override void SetOpen(bool open)
    {
        angleJoint.GetActuatorInstance().SetPosition(open ? openAngle : closeAngle);
    }

    public override bool IsSettled()
    {
        var inst = angleJoint.GetActuatorInstance();
        return Mathf.Abs(inst.GetPosition() - inst.GetTargetPosition()) < 1f;
    }

    [ContextMenu("Reset Gripper Angles")]
    void ResetGripperAngles()
    {
        openAngle  = 0f;
        closeAngle = -90f;
    }

    public override void Tick()
    {
        if (!_profileApplied)
        {
            if (overrideAcceleration)
            {
                var inst = angleJoint.GetActuatorInstance();
                if (inst is IActuatorProfile<DynamixelProfile> p)
                {
                    var prof = p.GetProfile();
                    prof.profileAcceleration = overrideAccelerationValue;
                    p.SetProfile(prof);
                }
            }
            _profileApplied = true;
        }
        SyncAngles();
    }

    private void SyncAngles()
    {
        float crankAngle = angleJoint.GetAnglePosition();

        float rodAngle = CalculateRodAngleRelToCrank(crankAngle);
        connectingRodL.localEulerAngles = new Vector3(rodAngle, 0, 0);
        connectingRodR.localEulerAngles = new Vector3(rodAngle, 0, 0);

        float sliderPos = CalculateSliderPosition(crankAngle);

        Vector3 posL = sliderL.localPosition;
        posL.z = -sliderPos;
        sliderL.localPosition = posL;

        Vector3 posR = sliderR.localPosition;
        posR.z = sliderPos;
        sliderR.localPosition = posR;
    }

    // Angle of the connecting rod relative to the crank, in degrees.
    // Derived from the crank-slider constraint:
    //   sin(β) = r·sin(θ) / l  →  β = arcsin(r·sin(θ) / l)
    // The rod's local angle relative to the crank is then (180° - θ - β).
    private float CalculateRodAngleRelToCrank(float crankAngle)
    {
        return 180f - crankAngle - CalculateRodAngle(crankAngle);
    }

    private float CalculateRodAngle(float crankAngle)
    {
        float rad    = crankAngle * Mathf.Deg2Rad;
        float sinVal = Mathf.Clamp(crankRadius * Mathf.Sin(rad) / connectingRodLength, -1f, 1f);
        return Mathf.Asin(sinVal) * Mathf.Rad2Deg;
    }

    // Slider displacement along its axis (crank-slider formula):
    //   x = r·cos(θ) + √(l² - r²·sin²(θ))
    private float CalculateSliderPosition(float crankAngle)
    {
        float rad     = crankAngle * Mathf.Deg2Rad;
        float sinCrank = Mathf.Sin(rad);
        float cosCrank = Mathf.Cos(rad);
        return crankRadius * cosCrank
               + Mathf.Sqrt(connectingRodLength * connectingRodLength
                             - crankRadius * crankRadius * sinCrank * sinCrank);
    }
}
