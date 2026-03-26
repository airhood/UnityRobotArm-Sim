using UnityEngine;

public enum RangeDirection { Both, PositiveOnly, NegativeOnly }

public class IKTest : MonoBehaviour
{
    [SerializeField] private Manipulator manipulator;
    [SerializeField] private Transform target;
    [SerializeField] private float rate;
    [SerializeField] private EndEffectorPathGizmo pathGizmo;
    
    [Header("Target Range")]
    [SerializeField] private Vector3 center;
    [SerializeField] private Vector3 minRange;
    [SerializeField] private Vector3 maxRange;
    [SerializeField] private RangeDirection dirX = RangeDirection.Both;
    [SerializeField] private RangeDirection dirY = RangeDirection.Both;
    [SerializeField] private RangeDirection dirZ = RangeDirection.Both;
    
    void Start()
    {
        InvokeRepeating("RandomTarget", 0, rate);
    }

    private void RandomTarget()
    {
        pathGizmo?.ClearPath();
        
        target.position = center + manipulator.transform.position + new Vector3(
            RandomExcludeCenter(minRange.x, maxRange.x, dirX),
            RandomExcludeCenter(minRange.y, maxRange.y, dirY),
            RandomExcludeCenter(minRange.z, maxRange.z, dirZ)
        );
    }

    private float RandomExcludeCenter(float min, float max, RangeDirection dir)
    {
        float value = Random.Range(min, max);
        return dir switch
        {
            RangeDirection.PositiveOnly => value,
            RangeDirection.NegativeOnly => -value,
            _ => Random.value > 0.5f ? value : -value
        };
    }
    
    private void OnDrawGizmos()
    {
        Vector3 manipulatorPos = manipulator.transform.position;
        
        Vector3 maxSize = maxRange * 2f;
        Vector3 minSize = minRange * 2f;
        Vector3 maxOffset = Vector3.zero;
        Vector3 minOffset = Vector3.zero;

        maxOffset.x = dirX == RangeDirection.PositiveOnly ? maxRange.x / 2f :
            dirX == RangeDirection.NegativeOnly ? -maxRange.x / 2f : 0f;
        maxOffset.y = dirY == RangeDirection.PositiveOnly ? maxRange.y / 2f :
            dirY == RangeDirection.NegativeOnly ? -maxRange.y / 2f : 0f;
        maxOffset.z = dirZ == RangeDirection.PositiveOnly ? maxRange.z / 2f :
            dirZ == RangeDirection.NegativeOnly ? -maxRange.z / 2f : 0f;

        if (dirX != RangeDirection.Both) maxSize.x /= 2f;
        if (dirY != RangeDirection.Both) maxSize.y /= 2f;
        if (dirZ != RangeDirection.Both) maxSize.z /= 2f;

        minOffset.x = dirX == RangeDirection.PositiveOnly ? minRange.x / 2f :
            dirX == RangeDirection.NegativeOnly ? -minRange.x / 2f : 0f;
        minOffset.y = dirY == RangeDirection.PositiveOnly ? minRange.y / 2f :
            dirY == RangeDirection.NegativeOnly ? -minRange.y / 2f : 0f;
        minOffset.z = dirZ == RangeDirection.PositiveOnly ? minRange.z / 2f :
            dirZ == RangeDirection.NegativeOnly ? -minRange.z / 2f : 0f;

        if (dirX != RangeDirection.Both) minSize.x /= 2f;
        if (dirY != RangeDirection.Both) minSize.y /= 2f;
        if (dirZ != RangeDirection.Both) minSize.z /= 2f;

        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawCube(center + manipulatorPos + maxOffset, maxSize);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center + manipulatorPos + maxOffset, maxSize);

        Gizmos.color = new Color(1f, 0.1f, 0f, 0.15f);
        Gizmos.DrawCube(center + manipulatorPos + minOffset, minSize);
        Gizmos.color = new Color(1f, 0.1f, 0f, 1f);
        Gizmos.DrawWireCube(center + manipulatorPos + minOffset, minSize);

        if (target != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(target.position, 0.02f);
        }
    }
}