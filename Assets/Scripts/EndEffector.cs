using UnityEngine;

public abstract class EndEffector : MonoBehaviour
{
    public virtual void Tick() { }
    public virtual void SetOpen(bool open) { }
    public virtual bool IsSettled() => true;
}
