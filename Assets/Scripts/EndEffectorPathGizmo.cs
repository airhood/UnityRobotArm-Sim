using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class EndEffectorPathGizmo : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Manipulator manipulator;

    [Header("Path Settings")]
    [SerializeField] private int maxPoints = 500;
    [SerializeField] private float minDistance = 0.001f;

    [Header("Gizmo Style")]
    [SerializeField] private Color pathColor = new Color(0.2f, 0.8f, 1f, 1f);
    [SerializeField] private float dotRadius = 0.005f;

    private readonly Queue<Vector3> _path = new Queue<Vector3>();
    private Vector3 _lastRecorded = Vector3.positiveInfinity;

    void Update()
    {
        if (manipulator.endPoint == null) return;

        Vector3 pos = manipulator.endPoint.position;
        if (Vector3.Distance(pos, _lastRecorded) < minDistance) return;

        _path.Enqueue(pos);
        _lastRecorded = pos;

        while (_path.Count > maxPoints)
            _path.Dequeue();
    }

    public void ClearPath() => _path.Clear();

    private void OnDrawGizmos()
    {
        if (_path.Count < 2) return;

        Vector3[] points = _path.ToArray();

#if UNITY_EDITOR
        for (int i = 1; i < points.Length; i++)
        {
            Handles.color = pathColor;
            Handles.DrawLine(points[i - 1], points[i], 2f);
        }
#else
    for (int i = 1; i < points.Length; i++)
    {
        float t = (float)i / points.Length;
        Color c = pathColor;
        c.a = pathColor.a * t;
        Gizmos.color = c;
        Gizmos.DrawLine(points[i - 1], points[i]);
    }
#endif

        if (manipulator.endPoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(manipulator.endPoint.position, dotRadius * 2f);
        }
    }
}