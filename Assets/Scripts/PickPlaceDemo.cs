using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Automated pick-and-place demo script.
///
/// - Spawns multiple objects and drop zones each episode
/// - Randomly selects one object and one zone as targets
/// - Label: "pick up the [color] [object] and place it on the [color] [zone]"
/// - Reports episode boundaries and phases to DataCollector
/// </summary>
public class PickPlaceDemo : MonoBehaviour
{
    [System.Serializable]
    public class PickablePrefab
    {
        public GameObject  prefab;
        [Tooltip("Object names used in label — one is picked randomly each episode (e.g. block, box, brick)")]
        public List<string> displayNames;
        public MaterialSet  materials;
    }

    [System.Serializable]
    public class DropZonePrefab
    {
        public GameObject  prefab;
        [Tooltip("Zone names used in label — one is picked randomly each episode (e.g. plate, tray, dish)")]
        public List<string> displayNames;
        public MaterialSet  materials;
    }

    [Header("References")]
    public Manipulator           manipulator;
    public IKReceiver            ikReceiver;
    public AICommandServer       aiCommandServer;
    public DataCollector         dataCollector;
    public EndEffectorPathGizmo  pathGizmo;

    [Header("Object Prefabs")]
    public List<PickablePrefab> prefabs;

    [Header("Drop Zone Prefabs")]
    public List<DropZonePrefab> zonePrefabs;

    [Header("Object Workspace (meters)")]
    public Vector2 workspaceX = new Vector2(-0.15f,  0.15f);
    public Vector2 workspaceY = new Vector2( 0.05f,  0.25f);
    public Vector2 workspaceZ = new Vector2( 0.08f,  0.22f);

    [Header("Table")]
    public float tableY         = 0f;
    public float tableThickness = 0.01f;
    public Color tableColor     = new Color(0.6f, 0.4f, 0.2f);

    [Header("Prompt Templates")]
    [Tooltip("{0} = object label, {1} = zone label")]
    public List<string> promptTemplates = new List<string>
    {
        "pick up the {0} and place it on the {1}",
        "grab the {0} and put it on the {1}",
        "move the {0} onto the {1}",
        "take the {0} and set it on the {1}",
        "place the {0} on the {1}",
        "bring the {0} to the {1}",
        "put the {0} on the {1}",
        "lift the {0} and drop it on the {1}",
        "carry the {0} to the {1}",
        "move the {0} to the {1}",
        "take the {0} over to the {1}",
        "drop the {0} at the {1}",
        "leave the {0} at the {1}",
        "set the {0} down at the {1}",
        "slide the {0} onto the {1}",
        "transfer the {0} to the {1}",
        "relocate the {0} to the {1}",
        "put the {0} down on the {1}",
        "pick the {0} up and bring it to the {1}",
        "grab the {0} and carry it to the {1}",
        "take the {0} and move it to the {1}",
        "pick the {0} up and move it onto the {1}",
        "get the {0} and place it at the {1}",
    };

    [Header("Spawn Settings")]
    [Tooltip("Minimum distance between any two spawned items (m)")]
    public float minSpawnDistance = 0.07f;
    [Tooltip("Max retries to find a valid spawn position")]
    public int   maxSpawnRetries  = 50;

    [Header("Spawn Exclusion Zone")]
    public bool    useExclusionZone = true;
    public Vector2 excludeRangeX    = new Vector2(-0.05f,  0.05f);
    public Vector2 excludeRangeY    = new Vector2( 0.00f,  0.30f);
    public Vector2 excludeRangeZ    = new Vector2( 0.00f,  0.08f);

    [Header("Motion Parameters")]
    public float preGraspClearance  = 0.12f;
    public float graspHeight        = 0.03f;
    public float liftHeight         = 0.18f;
    public float placeHeight        = 0.03f;
    [Tooltip("물체를 내려놓을 때 pivot 위에 추가 여유 높이 (collider 끼임 방지)")]
    public float placeHeightOffset  = 0.005f;
    public Vector3 homePosition     = new Vector3(0f, 0.20f, 0.10f);

    [Header("Demo Settings")]
    public float reachThreshold    = 0.012f;
    public float reachTimeout      = 6f;
    public float gripperPostDelay  = 0.2f;
    public float spawnSettleTime   = 0.4f;
    public float episodeInterval   = 0.3f;

    [Header("Grasp Settings")]
    public float attachDistance = 0.04f;

    // ── Runtime ───────────────────────────────────────────────────────────

    private struct SpawnedObject
    {
        public GameObject obj;
        public Transform  bottom;    // child named "bottom" — object base for placement height
        public Transform  top;       // child named "top"    — for gizmo marker display
        public string     colorName;
        public string     displayName;
    }

    private struct SpawnedZone
    {
        public GameObject obj;
        public Transform  pivot;    // child named "pivot"  — where the object is placed (zone top)
        public Transform  bottom;   // child named "bottom" — zone base for table alignment
        public string     colorName;
        public string     displayName;
    }

    private Transform           _ikTarget;
    private List<SpawnedObject> _objects  = new List<SpawnedObject>();
    private List<SpawnedZone>   _zones    = new List<SpawnedZone>();
    private int                 _targetObjIdx    = 0;
    private int                 _targetZoneIdx   = 0;
    private float               _objBottomOffset = 0f; // endPoint.y - object.bottom.y at attach time
    private GameObject          _table;
    private int                 _episodeId = 0;
    private bool                _attached  = false;

    private GameObject CurrentObject =>
        (_objects.Count > _targetObjIdx) ? _objects[_targetObjIdx].obj : null;

    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (prefabs == null || prefabs.Count == 0)
        { Debug.LogError("[PickPlaceDemo] Prefab list is empty."); return; }
        if (zonePrefabs == null || zonePrefabs.Count == 0)
        { Debug.LogError("[PickPlaceDemo] Drop zone list is empty."); return; }

        float cx = (workspaceX.x + workspaceX.y) * 0.5f;
        float cz = (workspaceZ.x + workspaceZ.y) * 0.5f;
        float sx = workspaceX.y - workspaceX.x;
        float sz = workspaceZ.y - workspaceZ.x;

        _table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _table.name = "Table (Dynamic)";
        _table.transform.position   = new Vector3(cx, workspaceY.x + tableY - tableThickness * 0.5f, cz);
        _table.transform.localScale = new Vector3(sx, tableThickness, sz);
        _table.GetComponent<Renderer>().material.color = tableColor;

        var go = new GameObject("IK Target (Dynamic)");
        go.transform.position = homePosition;
        _ikTarget = go.transform;
        ikReceiver.target = _ikTarget;
        if (aiCommandServer != null)
            aiCommandServer.targetTransform = _ikTarget;

        StartCoroutine(RunAllDemos());
    }

    void OnDestroy()
    {
        ClearObjects();
        ClearZones();
        if (_ikTarget != null) Destroy(_ikTarget.gameObject);
        if (_table    != null) Destroy(_table);
    }

    // ── Main loop ─────────────────────────────────────────────────────────

    IEnumerator RunAllDemos()
    {
        yield return MoveArm(homePosition);
        while (true)
        {
            yield return RunEpisode();
            _episodeId++;
            yield return new WaitForSeconds(episodeInterval);
        }
    }

    IEnumerator RunEpisode()
    {
        SpawnAll();
        yield return new WaitForSeconds(spawnSettleTime);

        _targetObjIdx  = Random.Range(0, _objects.Count);
        _targetZoneIdx = Random.Range(0, _zones.Count);

        SpawnedObject so = _objects[_targetObjIdx];
        SpawnedZone   sz = _zones[_targetZoneIdx];

        Vector3 objPos    = so.obj.transform.position;
        float   objTopY   = so.top.position.y;
        float   objBottomY = so.bottom.position.y;
        Vector3 zonePos   = sz.pivot.position;

        string objLabel  = BuildObjectLabel(so.colorName, so.displayName);
        string zoneLabel = BuildZoneLabel(sz.colorName, sz.displayName);
        string template  = promptTemplates.Count > 0
            ? promptTemplates[Random.Range(0, promptTemplates.Count)]
            : "pick up the {0} and place it on the {1}";
        string label = string.Format(template, objLabel, zoneLabel);

        pathGizmo?.ClearPath();
        dataCollector?.BeginEpisode(_episodeId, label);

        float absTableY = workspaceY.x + tableY;

        // 1. Open gripper
        dataCollector?.SetPhase("open_gripper");
        manipulator.SetGripperOpen(true);
        yield return WaitForGripper();

        // 2. Pre-grasp — well above the object
        dataCollector?.SetPhase("pre_grasp");
        yield return MoveArm(new Vector3(objPos.x, objTopY + preGraspClearance, objPos.z));

        // 3. Approach — descend to just above object top
        dataCollector?.SetPhase("approach");
        yield return MoveArm(new Vector3(objPos.x, objTopY, objPos.z));

        // 4. Lower — descend to grip height (gripper still open)
        dataCollector?.SetPhase("lower");
        yield return MoveArm(new Vector3(objPos.x, objBottomY + graspHeight, objPos.z));

        // 5. Grasp — close after fully lowered
        dataCollector?.SetPhase("grasp");
        manipulator.SetGripperOpen(false);
        yield return WaitForGripper();
        TryAttach();

        // 6. Lift
        dataCollector?.SetPhase("lift");
        yield return MoveArm(new Vector3(objPos.x, absTableY + liftHeight, objPos.z));

        // 7. Move above zone
        dataCollector?.SetPhase("move_to_zone");
        yield return MoveArm(new Vector3(zonePos.x, absTableY + liftHeight, zonePos.z));

        // 8. Lower to place height — end effector stops so object bottom lands on zone pivot
        dataCollector?.SetPhase("lower_to_zone");
        yield return MoveArm(new Vector3(zonePos.x, zonePos.y + _objBottomOffset + placeHeightOffset, zonePos.z));

        // 9. Release
        dataCollector?.SetPhase("release");
        Detach();
        manipulator.SetGripperOpen(true);
        yield return WaitForGripper();

        // 10. Return
        dataCollector?.SetPhase("return");
        yield return MoveArm(new Vector3(zonePos.x, absTableY + liftHeight, zonePos.z));
        yield return MoveArm(homePosition);

        dataCollector?.EndEpisode();

        ClearObjects();
        ClearZones();
    }

    // ── Attach / Detach ───────────────────────────────────────────────────

    void TryAttach()
    {
        if (CurrentObject == null) return;
        Transform pivot = manipulator.gripperPivot;
        float dist = Vector3.Distance(pivot.position, CurrentObject.transform.position);
        if (dist > attachDistance)
        {
            Debug.LogWarning($"[PickPlaceDemo] Grasp failed: dist={dist:F3}m > {attachDistance}m");
            return;
        }
        if (CurrentObject.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic     = true;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Compute bottom offset before parenting (world positions still independent)
        var bottom = _objects[_targetObjIdx].bottom;
        _objBottomOffset = pivot.position.y - bottom.position.y;

        CurrentObject.transform.SetParent(pivot, worldPositionStays: true);
        _attached = true;
        Debug.Log($"[PickPlaceDemo] Attached (dist={dist:F3}m, bottomOffset={_objBottomOffset:F3}m)");
    }

    void Detach()
    {
        if (!_attached || CurrentObject == null) return;
        CurrentObject.transform.SetParent(null, worldPositionStays: true);
        if (CurrentObject.TryGetComponent<Rigidbody>(out var rb))
            rb.isKinematic = false;
        _attached = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Generic nouns used when describing an object without its shape name
    static readonly string[] ObjGenericNouns =
    {
        "one", "object", "item", "thing", "piece",
    };

    // Generic nouns used when describing a zone without its shape name
    static readonly string[] ZoneGenericNouns =
    {
        "one", "spot", "area", "zone", "place", "location", "region", "side", "position",
    };

    string BuildObjectLabel(string color, string shape)
    {
        bool hasColor = !string.IsNullOrEmpty(color);
        bool hasShape = !string.IsNullOrEmpty(shape);
        string generic = ObjGenericNouns[Random.Range(0, ObjGenericNouns.Length)];

        if (hasColor && hasShape)
        {
            return Random.Range(0, 4) switch
            {
                0 => $"{color} {shape}",             // "red block"
                1 => $"{color} {generic}",           // "red thing"
                2 => shape,                          // "block"
                _ => $"{color}-colored {shape}",     // "red-colored block"
            };
        }
        if (hasColor) return $"{color} {generic}";
        return shape;
    }

    string BuildZoneLabel(string color, string shape)
    {
        bool hasColor = !string.IsNullOrEmpty(color);
        bool hasShape = !string.IsNullOrEmpty(shape);
        string generic = ZoneGenericNouns[Random.Range(0, ZoneGenericNouns.Length)];

        if (hasColor && hasShape)
        {
            return Random.Range(0, 4) switch
            {
                0 => $"{color} {shape}",             // "blue plate"
                1 => $"{color} {generic}",           // "blue area"
                2 => shape,                          // "plate"
                _ => $"{color}-colored {shape}",     // "blue-colored plate"
            };
        }
        if (hasColor) return $"{color} {generic}";
        return shape;
    }

    void SpawnAll()
    {
        ClearObjects();
        ClearZones();

        var occupied = new List<Vector2>();

        int objN  = prefabs.Count;
        int zoneN = zonePrefabs.Count;
        var objIdx  = ShuffledRange(prefabs.Count);
        var zoneIdx = ShuffledRange(zonePrefabs.Count);

        for (int i = 0; i < objN; i++)
        {
            var p  = prefabs[objIdx[i]];
            var xz = FindSpawnXZ(workspaceX, workspaceZ, occupied);

            var obj = Instantiate(p.prefab,
                new Vector3(xz.x, workspaceY.x + tableY + 0.02f, xz.y),
                Quaternion.Euler(0, Random.Range(0f, 360f), 0));

            if (obj.TryGetComponent<Rigidbody>(out var rb))
            { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            ClampToBounds(obj, workspaceX, workspaceZ);
            occupied.Add(new Vector2(obj.transform.position.x, obj.transform.position.z));

            var bottomTf = obj.transform.Find("bottom") ?? obj.transform;
            var topTf    = obj.transform.Find("top")    ?? obj.transform;
            string color = ApplyRandomMaterial(obj, p.materials);
            string name  = PickRandom(p.displayNames);
            _objects.Add(new SpawnedObject { obj = obj, bottom = bottomTf, top = topTf, colorName = color, displayName = name });
        }

        for (int i = 0; i < zoneN; i++)
        {
            var z  = zonePrefabs[zoneIdx[i]];
            var xz = FindSpawnXZ(workspaceX, workspaceZ, occupied);

            var obj = Instantiate(z.prefab,
                new Vector3(xz.x, workspaceY.x + tableY, xz.y),
                Quaternion.identity);

            // Zones are static — disable physics if present
            if (obj.TryGetComponent<Rigidbody>(out var rb))
            { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            var bottomTf = obj.transform.Find("bottom") ?? obj.transform;
            var pivotTf  = obj.transform.Find("pivot")  ?? obj.transform;

            // Align zone so its bottom sits on the table surface
            float tableWorldY = workspaceY.x + tableY;
            obj.transform.position += Vector3.up * (tableWorldY - bottomTf.position.y);

            ClampToBounds(obj, workspaceX, workspaceZ);
            occupied.Add(new Vector2(obj.transform.position.x, obj.transform.position.z));

            string color = ApplyRandomMaterial(obj, z.materials);
            string name  = PickRandom(z.displayNames);
            _zones.Add(new SpawnedZone { obj = obj, pivot = pivotTf, bottom = bottomTf, colorName = color, displayName = name });
        }
    }

    Vector2 FindSpawnXZ(Vector2 rangeX, Vector2 rangeZ, List<Vector2> occupied)
    {
        for (int retry = 0; retry < maxSpawnRetries; retry++)
        {
            float x = Random.Range(rangeX.x, rangeX.y);
            float z = Random.Range(rangeZ.x, rangeZ.y);

            if (useExclusionZone && IsExcluded(x, z)) continue;

            bool tooClose = false;
            foreach (var occ in occupied)
            {
                if (Vector2.Distance(new Vector2(x, z), occ) < minSpawnDistance)
                { tooClose = true; break; }
            }
            if (!tooClose) return new Vector2(x, z);
        }

        Debug.LogWarning("[PickPlaceDemo] Could not find a valid spawn position.");
        return new Vector2(Random.Range(rangeX.x, rangeX.y), Random.Range(rangeZ.x, rangeZ.y));
    }

    string ApplyRandomMaterial(GameObject obj, MaterialSet set)
    {
        if (set == null || set.materials == null || set.materials.Count == 0) return "";
        var pick = set.materials[Random.Range(0, set.materials.Count)];
        if (pick.material != null)
        {
            var rend = obj.GetComponentInChildren<Renderer>();
            if (rend != null) rend.material = pick.material;
        }
        return pick.colorName ?? "";
    }

    string PickRandom(List<string> list) =>
        (list != null && list.Count > 0) ? list[Random.Range(0, list.Count)] : "";

    // Shift obj in XZ so its renderer bounds stay fully inside rangeX / rangeZ.
    void ClampToBounds(GameObject obj, Vector2 rangeX, Vector2 rangeZ)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 pos = obj.transform.position;
        float dx = 0f, dz = 0f;

        if      (bounds.min.x < rangeX.x) dx = rangeX.x - bounds.min.x;
        else if (bounds.max.x > rangeX.y) dx = rangeX.y - bounds.max.x;

        if      (bounds.min.z < rangeZ.x) dz = rangeZ.x - bounds.min.z;
        else if (bounds.max.z > rangeZ.y) dz = rangeZ.y - bounds.max.z;

        if (dx != 0f || dz != 0f)
            obj.transform.position = new Vector3(pos.x + dx, pos.y, pos.z + dz);
    }

    bool IsExcluded(float x, float z) =>
        x >= excludeRangeX.x && x <= excludeRangeX.y &&
        z >= excludeRangeZ.x && z <= excludeRangeZ.y;

    List<int> ShuffledRange(int count)
    {
        var list = new List<int>();
        for (int i = 0; i < count; i++) list.Add(i);
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    void ClearObjects()
    {
        foreach (var s in _objects)
            if (s.obj != null) Destroy(s.obj);
        _objects.Clear();
        _attached = false;
    }

    void ClearZones()
    {
        foreach (var z in _zones)
            if (z.obj != null) Destroy(z.obj);
        _zones.Clear();
    }

    IEnumerator WaitForGripper()
    {
        while (!manipulator.IsGripperSettled())
            yield return null;
        yield return new WaitForSeconds(gripperPostDelay);
    }

    IEnumerator MoveArm(Vector3 target)
    {
        _ikTarget.position = target;
        float elapsed = 0f;
        while (elapsed < reachTimeout)
        {
            elapsed += Time.deltaTime;
            if (Vector3.Distance(manipulator.endPoint.position, target) < reachThreshold) break;
            yield return null;
        }
        yield return new WaitForSeconds(0.08f);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        float absTableY = workspaceY.x + tableY;
        float cy = (workspaceY.x + workspaceY.y) * 0.5f;
        float sy = workspaceY.y - workspaceY.x;

        // Table (edit mode only)
        if (!Application.isPlaying)
        {
            float cx = (workspaceX.x + workspaceX.y) * 0.5f;
            float cz = (workspaceZ.x + workspaceZ.y) * 0.5f;
            float sx = workspaceX.y - workspaceX.x;
            float sz = workspaceZ.y - workspaceZ.x;
            Vector3 tableCenter = new Vector3(cx, absTableY - tableThickness * 0.5f, cz);
            Vector3 tableSize   = new Vector3(sx, tableThickness, sz);
            Gizmos.color = new Color(1f, 0f, 1f, 0.4f);
            Gizmos.DrawCube(tableCenter, tableSize);
            Gizmos.color = new Color(1f, 0f, 1f, 1f);
            Gizmos.DrawWireCube(tableCenter, tableSize);
        }

        // Object workspace (green)
        {
            float cx = (workspaceX.x + workspaceX.y) * 0.5f;
            float cz = (workspaceZ.x + workspaceZ.y) * 0.5f;
            float sx = workspaceX.y - workspaceX.x;
            float sz = workspaceZ.y - workspaceZ.x;
            if (!Application.isPlaying)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.08f);
                Gizmos.DrawCube(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));
            }
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));
        }

        // Exclusion zone (orange)
        if (useExclusionZone)
        {
            float ecx = (excludeRangeX.x + excludeRangeX.y) * 0.5f;
            float ecy = workspaceY.x + tableY + (excludeRangeY.x + excludeRangeY.y) * 0.5f;
            float ecz = (excludeRangeZ.x + excludeRangeZ.y) * 0.5f;
            float esx = excludeRangeX.y - excludeRangeX.x;
            float esy = excludeRangeY.y - excludeRangeY.x;
            float esz = excludeRangeZ.y - excludeRangeZ.x;
            if (!Application.isPlaying)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.08f);
                Gizmos.DrawCube(new Vector3(ecx, ecy, ecz), new Vector3(esx, esy, esz));
            }
            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Gizmos.DrawWireCube(new Vector3(ecx, ecy, ecz), new Vector3(esx, esy, esz));
        }

        // ── Runtime markers ───────────────────────────────────────────────
        if (!Application.isPlaying) return;

        // Target object marker (yellow sphere at top)
        if (_objects.Count > _targetObjIdx && _objects[_targetObjIdx].obj != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_objects[_targetObjIdx].top.position, 0.008f);
        }

        // Target zone marker (cyan sphere at pivot)
        if (_zones.Count > _targetZoneIdx && _zones[_targetZoneIdx].obj != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(_zones[_targetZoneIdx].pivot.position, 0.015f);
        }
    }
}
