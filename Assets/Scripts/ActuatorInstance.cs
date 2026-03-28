using UnityEngine;

public abstract class ActuatorInstance
{
    private Actuator   actuator;
    private AngleJoint angleJoint;

    protected float anglePosition;
    protected float targetAnglePosition;

    public ActuatorInstance(Actuator actuator, AngleJoint angleJoint)
    {
        this.actuator   = actuator;
        this.angleJoint = angleJoint;
        anglePosition   = 0;
    }

    // dt = Time.deltaTime (or fixed delta). Caller is responsible for passing correct value.
    public abstract void Tick(float dt);

    public void  SetInitialPosition(float position) => anglePosition       = position;
    public void  SetPosition(float position)        => targetAnglePosition = position;
    public float GetPosition()                      => anglePosition;
    public float GetTargetPosition()                => targetAnglePosition;
}

public interface IActuatorProfile<TConfig> where TConfig : struct
{
    TConfig GetProfile();
    void    SetProfile(TConfig config);
}