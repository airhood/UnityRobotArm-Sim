using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central simulation tick dispatcher.
/// Components register a callback with a priority (lower = earlier).
/// SimCore dispatches all registered callbacks every FixedUpdate.
/// </summary>
public class SimCore : MonoBehaviour
{
    public static SimCore Instance { get; private set; }

    private struct Entry
    {
        public int                  priority;
        public System.Action<float> tick;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private bool _dirty = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        { Debug.LogError("[SimCore] Multiple instances detected."); return; }
        Instance = this;
    }

    public void Register(System.Action<float> tick, int priority = 0)
    {
        _entries.Add(new Entry { priority = priority, tick = tick });
        _dirty = true;
    }

    public void Unregister(System.Action<float> tick)
    {
        _entries.RemoveAll(e => e.tick == tick);
    }

    void FixedUpdate()
    {
        if (_dirty)
        {
            _entries.Sort((a, b) => a.priority.CompareTo(b.priority));
            _dirty = false;
        }
        float dt = Time.fixedDeltaTime;
        for (int i = 0; i < _entries.Count; i++)
            _entries[i].tick(dt);
    }
}
