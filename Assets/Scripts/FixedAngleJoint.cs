using Unity.Burst.Intrinsics;
using UnityEngine;

[System.Serializable]
public class FixedAngleJoint
{
    [System.Serializable]
    public enum Axis
    {
        X, Y, Z
    }
    
    public Axis axis;
    public float angle;
}