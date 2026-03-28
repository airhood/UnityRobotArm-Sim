using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SBC (Single-Board Computer) layer.
/// Reads joint angle targets from IKReceiver and drives actuators.
/// Handles Dynamixel motion profile configuration and joint sync.
/// </summary>
public class SBCController : MonoBehaviour
{
    [Header("References")]
    public Manipulator manipulator;
    public IKReceiver  ikReceiver;

    [Header("Dynamixel Profile")]
    public int profileVelocity     = 100;
    public int profileAcceleration = 7;

    [Header("Joint Synchronization")]
    [Tooltip("모든 관절이 동시에 목표에 도달하도록 profileVelocity를 비례 조정")]
    public bool  syncJoints       = true;
    [Tooltip("이 각도(도) 이상 변화 시 동기화 재계산")]
    public float syncTriggerDelta = 2f;

    private float[] _lastSyncAngles;

    void Start()
    {
        if (SimCore.Instance == null)
        { Debug.LogError("[SBCController] SimCore not found."); return; }

        ConfigureActuators();
        SimCore.Instance.Register(Tick, priority: 0); // before Manipulator (priority 10)
    }

    void OnDestroy()
    {
        SimCore.Instance?.Unregister(Tick);
    }

    void ConfigureActuators()
    {
        foreach (var inst in manipulator.GetActuatorInstances())
        {
            if (inst is IActuatorProfile<DynamixelProfile> p)
            {
                var profile = p.GetProfile();
                profile.profileVelocity     = profileVelocity;
                profile.profileAcceleration = profileAcceleration;
                p.SetProfile(profile);
            }
        }
    }

    void Tick(float dt)
    {
        float[] latestAngles = ikReceiver.LastJointAngles;
        if (latestAngles == null) return;

        List<ActuatorInstance> actuatorInstances = manipulator.GetActuatorInstances();

        // ── Joint Synchronization ────────────────────────────────────────
        if (syncJoints)
        {
            bool triggered = _lastSyncAngles == null;
            if (!triggered && _lastSyncAngles != null)
            {
                float maxChange = 0f;
                for (int i = 0; i < latestAngles.Length && i < _lastSyncAngles.Length; i++)
                    maxChange = Mathf.Max(maxChange, Mathf.Abs(latestAngles[i] - _lastSyncAngles[i]));
                triggered = maxChange > syncTriggerDelta;
            }

            if (triggered)
            {
                _lastSyncAngles = (float[])latestAngles.Clone();

                float maxDelta = 0f;
                float[] deltas = new float[actuatorInstances.Count];
                for (int i = 0; i < actuatorInstances.Count && i < latestAngles.Length; i++)
                {
                    float d = Mathf.Abs(latestAngles[i] - actuatorInstances[i].GetPosition());
                    if (d > 180f) d = 360f - d;
                    deltas[i] = d;
                    if (d > maxDelta) maxDelta = d;
                }

                if (maxDelta > 0.5f)
                {
                    float vRefDeg = profileVelocity     * DynamixelProfile.RPM_PER_VELOCITY_TICK  * 360f / 60f;
                    float aRefDeg = profileAcceleration * DynamixelProfile.REVMIN2_PER_ACCEL_TICK * 360f / 3600f;
                    float tRef    = TravelTime(maxDelta, vRefDeg, aRefDeg);

                    for (int i = 0; i < actuatorInstances.Count && i < latestAngles.Length; i++)
                    {
                        if (actuatorInstances[i] is IActuatorProfile<DynamixelProfile> p)
                        {
                            float scale  = deltas[i] / maxDelta;
                            int   vTicks = Mathf.Max(1, Mathf.RoundToInt(profileVelocity * scale));
                            float vDeg   = vTicks * DynamixelProfile.RPM_PER_VELOCITY_TICK * 360f / 60f;

                            float aTickScale = DynamixelProfile.REVMIN2_PER_ACCEL_TICK * 360f / 3600f;
                            float aOptTicks  = BestAccelTicks(deltas[i], vDeg, tRef, aTickScale);
                            int   aTicks     = Mathf.Max(1, Mathf.RoundToInt(aOptTicks));

                            var prof = p.GetProfile();
                            prof.profileVelocity     = vTicks;
                            prof.profileAcceleration = aTicks;
                            p.SetProfile(prof);
                        }
                    }
                }
            }
        }
        // ─────────────────────────────────────────────────────────────────

        for (int i = 0; i < actuatorInstances.Count && i < latestAngles.Length; i++)
            actuatorInstances[i].SetPosition(latestAngles[i]);
    }

    static float TravelTime(float dist, float vMax, float accel)
    {
        if (accel <= 0f) return dist / vMax;
        float v2a = vMax * vMax / accel;
        return (dist <= v2a)
            ? 2f * Mathf.Sqrt(dist / accel)
            : dist / vMax + vMax / accel;
    }

    static float BestAccelTicks(float dist, float vDeg, float tTarget, float aTickScale)
    {
        float aOptDeg   = 4f * dist / (tTarget * tTarget);
        float aOptTicks = aOptDeg / aTickScale;
        int aFloor = Mathf.Max(0, (int)aOptTicks);
        int aCeil  = aFloor + 1;
        float tFloor = TravelTime(dist, vDeg, aFloor * aTickScale);
        float tCeil  = TravelTime(dist, vDeg, aCeil  * aTickScale);
        if (tFloor > tTarget + 0.001f) return aCeil;
        return Mathf.Abs(tFloor - tTarget) <= Mathf.Abs(tCeil - tTarget) ? aFloor : aCeil;
    }
}
