using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자동 pick-and-place 시연 스크립트.
///
/// - Prefab 리스트를 런타임에 Instantiate → 한 번에 하나만 씬에 존재
/// - IK Target을 동적으로 생성해 IKReceiver / AICommandServer 에 주입
/// - DataCollector 에 에피소드 구간(label, phase)을 알려 학습 데이터 자동 수집
///
/// Inspector 에서 기존 'IK Target' 오브젝트를 비활성화해두면 된다.
/// </summary>
public class PickPlaceDemo : MonoBehaviour
{
    [System.Serializable]
    public class NamedMaterial
    {
        public Material material;
        [Tooltip("레이블에 쓸 색 이름 (예: red, blue, green)")]
        public string colorName;
    }

    [System.Serializable]
    public class PickablePrefab
    {
        public GameObject prefab;
        [Tooltip("학습 레이블에 쓸 물체 이름 (예: block, cube, cylinder)")]
        public string displayName;
        [Tooltip("에피소드마다 랜덤 적용할 Material 목록")]
        public List<NamedMaterial> materials;
    }

    [Header("References")]
    public IKReceiver            ikReceiver;
    public AICommandServer       aiCommandServer;   // IK Target 교체 시 같이 업데이트
    public Transform             endPoint;
    public SliderGripper         gripper;
    public DataCollector         dataCollector;
    public EndEffectorPathGizmo  pathGizmo;

    [Header("Object Prefabs (런타임 생성)")]
    public List<PickablePrefab> prefabs;

    [Header("Workspace (Unity world coords, meters)")]
    public Vector2 workspaceX = new Vector2(-0.15f,  0.15f);
    public Vector2 workspaceY = new Vector2( 0.05f,  0.25f);
    public Vector2 workspaceZ = new Vector2( 0.08f,  0.22f);

    [Header("Table (동적 생성)")]
    [Tooltip("테이블 표면 높이 — workspaceY 바닥(workspaceY.x) 기준 상대 높이 (m)")]
    public float tableY = 0f;
    [Tooltip("테이블 두께 (m)")]
    public float tableThickness = 0.01f;
    [Tooltip("테이블 색상")]
    public Color tableColor = new Color(0.6f, 0.4f, 0.2f);

    [Header("Spawn Exclusion Zone (로봇 베이스 도달 불가 영역)")]
    [Tooltip("제외 영역 활성화 여부")]
    public bool useExclusionZone = true;
    [Tooltip("제외할 X 범위 (min, max) — 절대 좌표")]
    public Vector2 excludeRangeX = new Vector2(-0.05f,  0.05f);
    [Tooltip("제외할 Y 범위 (min, max) — tableY 기준 상대 높이")]
    public Vector2 excludeRangeY = new Vector2( 0.00f,  0.30f);
    [Tooltip("제외할 Z 범위 (min, max) — 절대 좌표")]
    public Vector2 excludeRangeZ = new Vector2( 0.00f,  0.08f);
    [Tooltip("유효 스폰 위치를 찾기 위한 최대 재시도 횟수")]
    public int maxSpawnRetries = 30;

    [Header("Motion Parameters")]
    public float preGraspClearance = 0.12f;
    public float graspHeight       = 0.03f;
    public float liftHeight        = 0.18f;
    public Vector3 homePosition    = new Vector3(0f, 0.20f, 0.10f);

    [Header("Demo Settings")]
    public int   episodesPerObject  = 100;
    public float reachThreshold     = 0.012f;
    public float reachTimeout       = 6f;
    public float gripperSettleTime  = 0.5f;
    public float spawnSettleTime    = 0.4f;
    public float episodeInterval    = 0.3f;

    [Header("Grasp Settings")]
    [Tooltip("이 거리 이내일 때 그리퍼 닫으면 물체가 attach됨 (m)")]
    public float attachDistance = 0.04f;

    // ── 런타임 전용 ───────────────────────────────────────────────────────
    private Transform   _ikTarget;        // 동적으로 생성한 IK Target
    private GameObject  _currentObject;   // 현재 씬에 있는 물체 인스턴스
    private GameObject  _table;           // 동적으로 생성한 테이블
    private int         _episodeId = 0;
    private bool        _attached  = false;

    // ─────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (prefabs == null || prefabs.Count == 0)
        {
            Debug.LogError("[PickPlaceDemo] Prefab 리스트가 비어 있습니다.");
            return;
        }

        // 테이블 동적 생성 (workspaceX/Z 크기, tableY 절대 좌표 기준)
        float cx = (workspaceX.x + workspaceX.y) * 0.5f;
        float cz = (workspaceZ.x + workspaceZ.y) * 0.5f;
        float sx = workspaceX.y - workspaceX.x;
        float sz = workspaceZ.y - workspaceZ.x;

        _table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _table.name = "Table (Dynamic)";
        _table.transform.position   = new Vector3(cx, workspaceY.x + tableY - tableThickness * 0.5f, cz);
        _table.transform.localScale = new Vector3(sx, tableThickness, sz);

        _table.GetComponent<Renderer>().material.color = tableColor;

        // IK Target 동적 생성 후 IKReceiver / AICommandServer 에 주입
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
        if (_currentObject != null) Destroy(_currentObject);
        if (_ikTarget      != null) Destroy(_ikTarget.gameObject);
        if (_table         != null) Destroy(_table);
    }

    // ── 메인 루프 ─────────────────────────────────────────────────────────

    IEnumerator RunAllDemos()
    {
        yield return MoveArm(homePosition);

        foreach (var p in prefabs)
        {
            for (int i = 0; i < episodesPerObject; i++)
            {
                yield return RunEpisode(p);
                _episodeId++;
                yield return new WaitForSeconds(episodeInterval);
            }
        }

        Debug.Log($"[PickPlaceDemo] 완료. 총 에피소드: {_episodeId}");
    }

    IEnumerator RunEpisode(PickablePrefab p)
    {
        // ── 물체 생성 / 재배치 ────────────────────────────────────────────
        string colorName = SpawnObject(p);
        yield return new WaitForSeconds(spawnSettleTime);

        Vector3 objPos = _currentObject.transform.position;
        string  label  = string.IsNullOrEmpty(colorName)
            ? $"pick up the {p.displayName}"
            : $"pick up the {colorName} {p.displayName}";

        // ── 에피소드 시작 ─────────────────────────────────────────────────
        pathGizmo?.ClearPath();
        dataCollector?.BeginEpisode(_episodeId, label);

        // 1. 그리퍼 열기
        dataCollector?.SetPhase("open_gripper");
        gripper?.SetGripperOpen(true);
        yield return new WaitForSeconds(gripperSettleTime);

        // 2. Pre-grasp
        dataCollector?.SetPhase("pre_grasp");
        float absTableY = workspaceY.x + tableY;
        yield return MoveArm(new Vector3(objPos.x, absTableY + preGraspClearance, objPos.z));

        // 3. Approach
        dataCollector?.SetPhase("approach");
        yield return MoveArm(new Vector3(objPos.x, absTableY + graspHeight, objPos.z));

        // 4. Grasp
        dataCollector?.SetPhase("grasp");
        gripper?.SetGripperOpen(false);
        yield return new WaitForSeconds(gripperSettleTime);
        TryAttach();

        // 5. Lift
        dataCollector?.SetPhase("lift");
        yield return MoveArm(new Vector3(objPos.x, absTableY + liftHeight, objPos.z));

        // 6. 홈 복귀
        dataCollector?.SetPhase("return");
        yield return MoveArm(homePosition);

        // ── 에피소드 종료 ─────────────────────────────────────────────────
        dataCollector?.EndEpisode();

        Detach();
        gripper?.SetGripperOpen(true);

        // 물체 제거 (다음 에피소드에서 새로 생성)
        if (_currentObject != null)
        {
            Destroy(_currentObject);
            _currentObject = null;
        }
    }

    // ── Attach / Detach ───────────────────────────────────────────────────

    void TryAttach()
    {
        if (_currentObject == null) return;

        float dist = Vector3.Distance(endPoint.position, _currentObject.transform.position);
        if (dist > attachDistance)
        {
            Debug.LogWarning($"[PickPlaceDemo] Grasp failed: distance {dist:F3}m > {attachDistance}m");
            return;
        }

        // 물체 위치를 endPoint에 스냅 후 동기화 시작
        if (_currentObject.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic     = true;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        _currentObject.transform.position = endPoint.position;
        _attached = true;
        Debug.Log($"[PickPlaceDemo] Attached (dist={dist:F3}m)");
    }

    void Detach()
    {
        if (!_attached || _currentObject == null) return;

        if (_currentObject.TryGetComponent<Rigidbody>(out var rb))
            rb.isKinematic = false;

        _attached = false;
    }

    void LateUpdate()
    {
        // attach 중에는 매 프레임 endPoint 위치로 동기화
        if (_attached && _currentObject != null)
            _currentObject.transform.position = endPoint.position;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────

    // returns the chosen colorName (empty string if no materials assigned)
    string SpawnObject(PickablePrefab p)
    {
        if (_currentObject != null)
            Destroy(_currentObject);

        float x, z;
        int retries = 0;
        do
        {
            x = Random.Range(workspaceX.x, workspaceX.y);
            z = Random.Range(workspaceZ.x, workspaceZ.y);
            retries++;
        }
        while (useExclusionZone && IsExcluded(x, z) && retries < maxSpawnRetries);

        if (useExclusionZone && IsExcluded(x, z))
            Debug.LogWarning("[PickPlaceDemo] 제외 영역을 피한 스폰 위치를 찾지 못했습니다. 제외 영역 설정을 확인하세요.");

        _currentObject = Instantiate(
            p.prefab,
            new Vector3(x, workspaceY.x + tableY + 0.02f, z),
            Quaternion.Euler(0, Random.Range(0f, 360f), 0)
        );

        if (_currentObject.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // 랜덤 Material 적용
        if (p.materials != null && p.materials.Count > 0)
        {
            var pick = p.materials[Random.Range(0, p.materials.Count)];
            if (pick.material != null &&
                _currentObject.TryGetComponent<Renderer>(out var rend))
            {
                rend.material = pick.material;
            }
            return pick.colorName ?? "";
        }

        return "";
    }

    // X, Z만 체크 — 스폰 Y는 항상 tableY로 고정이므로 Y 범위 체크 불필요
    bool IsExcluded(float x, float z)
    {
        return x >= excludeRangeX.x && x <= excludeRangeX.y
            && z >= excludeRangeZ.x && z <= excludeRangeZ.y;
    }

    IEnumerator MoveArm(Vector3 target)
    {
        _ikTarget.position = target;

        float elapsed = 0f;
        while (elapsed < reachTimeout)
        {
            elapsed += Time.deltaTime;
            if (Vector3.Distance(endPoint.position, target) < reachThreshold)
                break;
            yield return null;
        }

        yield return new WaitForSeconds(0.08f);
    }

    // ── Gizmos ───────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        float cx = (workspaceX.x + workspaceX.y) * 0.5f;
        float cy = (workspaceY.x + workspaceY.y) * 0.5f;
        float cz = (workspaceZ.x + workspaceZ.y) * 0.5f;
        float sx = workspaceX.y - workspaceX.x;
        float sy = workspaceY.y - workspaceY.x;
        float sz = workspaceZ.y - workspaceZ.x;

        float absTableY = workspaceY.x + tableY;

        // 테이블 (tableColor, workspaceX/Z 크기, tableThickness 높이) ────
        float   tableCX = (workspaceX.x + workspaceX.y) * 0.5f;
        float   tableCZ = (workspaceZ.x + workspaceZ.y) * 0.5f;
        Vector3 tableCenter = new Vector3(tableCX, absTableY - tableThickness * 0.5f, tableCZ);
        Vector3 tableSize   = new Vector3(workspaceX.y - workspaceX.x, tableThickness, workspaceZ.y - workspaceZ.x);

        // 테이블 (마젠타) ────────────────────────────────────────────────
        Gizmos.color = new Color(1f, 0f, 1f, 0.4f);
        Gizmos.DrawCube(tableCenter, tableSize);
        Gizmos.color = new Color(1f, 0f, 1f, 1f);
        Gizmos.DrawWireCube(tableCenter, tableSize);

        // 워크스페이스 큐브 (초록색) ─────────────────────────────────────
        Vector3 wsCenter = new Vector3(cx, cy, cz);
        Vector3 wsSize   = new Vector3(sx, sy, sz);

        Gizmos.color = new Color(0f, 1f, 0f, 0.08f);
        Gizmos.DrawCube(wsCenter, wsSize);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(wsCenter, wsSize);

        // 제외 영역 큐브 (주황색, excludeRangeY는 tableY 기준 상대 높이) ─
        if (useExclusionZone)
        {
            float ecx = (excludeRangeX.x + excludeRangeX.y) * 0.5f;
            float ecy = workspaceY.x + tableY + (excludeRangeY.x + excludeRangeY.y) * 0.5f;
            float ecz = (excludeRangeZ.x + excludeRangeZ.y) * 0.5f;
            float esx = excludeRangeX.y - excludeRangeX.x;
            float esy = excludeRangeY.y - excludeRangeY.x;
            float esz = excludeRangeZ.y - excludeRangeZ.x;

            Vector3 excCenter = new Vector3(ecx, ecy, ecz);
            Vector3 excSize   = new Vector3(esx, esy, esz);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.08f);
            Gizmos.DrawCube(excCenter, excSize);
            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Gizmos.DrawWireCube(excCenter, excSize);
        }
    }
}
