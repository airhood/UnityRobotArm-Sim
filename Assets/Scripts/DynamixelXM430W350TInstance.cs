using UnityEngine;

public struct DynamixelProfile
{
    public const float RPM_PER_VELOCITY_TICK    = 0.229f;
    public const float REVMIN2_PER_ACCEL_TICK   = 214.577f;

    public int profileVelocity;
    public int profileAcceleration;

    public float VelocityDegPerSec =>
        profileVelocity * RPM_PER_VELOCITY_TICK * 360f / 60f;

    public float AccelerationDegPerSec2 =>
        profileAcceleration * REVMIN2_PER_ACCEL_TICK * 360f / 3600f;

    public static DynamixelProfile Default => new DynamixelProfile
    {
        profileVelocity     = 0,
        profileAcceleration = 0,
    };
}

public class DynamixelXM430W350TInstance : ActuatorInstance, IActuatorProfile<DynamixelProfile>
{
    private DynamixelProfile _profile = DynamixelProfile.Default;
    public DynamixelProfile GetProfile()                   => _profile;
    public void             SetProfile(DynamixelProfile p) => _profile = p;

    private readonly float _maxDegPerSec;
    private readonly bool  _hasAngleLimits;
    private readonly float _minAngle;
    private readonly float _maxAngle;

    private float _currentVelocity = 0f;
    private float _lastTarget      = float.NaN;

    public DynamixelXM430W350TInstance(Actuator actuator, AngleJoint angleJoint)
        : base(actuator, angleJoint)
    {
        _maxDegPerSec   = actuator.maxSpeed * 360f / 60f;  // RPM → deg/s
        _hasAngleLimits = actuator.angleRange.hasLimits;
        _minAngle       = actuator.angleRange.minAngle;
        _maxAngle       = actuator.angleRange.maxAngle;
    }

    public override void Tick(float dt)
    {
        float vMax = (_profile.profileVelocity <= 0)
            ? _maxDegPerSec
            : Mathf.Min(_profile.VelocityDegPerSec, _maxDegPerSec);

        bool  instantRamp = _profile.profileAcceleration <= 0;
        float accel       = _profile.AccelerationDegPerSec2;

        float clampedTarget = _hasAngleLimits
            ? Mathf.Clamp(targetAnglePosition, _minAngle, _maxAngle)
            : targetAnglePosition;

        if (!Mathf.Approximately(_lastTarget, clampedTarget))
            _lastTarget = clampedTarget;

        float remaining = clampedTarget - anglePosition;
        if (Mathf.Approximately(remaining, 0f)) { _currentVelocity = 0f; return; }

        float direction = Mathf.Sign(remaining);
        float dist      = Mathf.Abs(remaining);

        if (instantRamp)
        {
            float step = vMax * dt;
            if (dist <= step) { anglePosition = clampedTarget;            _currentVelocity = 0f; }
            else              { anglePosition += direction * step;    _currentVelocity = direction * vMax; }
            return;
        }

        float vCur   = Mathf.Abs(_currentVelocity);
        float dBrake = (vCur * vCur) / (2f * accel);

        float vDesired = (dBrake >= dist)
            ? Mathf.Min(Mathf.Sqrt(2f * accel * dist), vMax)
            : vMax;

        float vNext = (vDesired > vCur)
            ? Mathf.Min(vCur + accel * dt, vDesired)
            : Mathf.Max(vCur - accel * dt, vDesired);
        vNext = Mathf.Max(vNext, 0f);

        float move = vNext * dt;
        if (move >= dist) { anglePosition = clampedTarget;            _currentVelocity = 0f; }
        else              { anglePosition += direction * move;    _currentVelocity = direction * vNext; }
    }
}