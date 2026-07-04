using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class LevelTarget : MonoBehaviour
{
    [Header("移动路径（在 Scene 视图中可见）")]
    public List<Vector2> waypoints = new List<Vector2>();

    [Header("移动速度（世界单位/秒）")]
    public float moveSpeed = 2f;

    [Header("颜色")]
    public Color normalColor = new Color(1f, 0.85f, 0.1f);
    public Color hookedColor = new Color(0.3f, 1f, 0.45f);

    // ── 状态 ──────────────────────────────────────────────────
    public bool IsHooked { get; private set; }
    public Vector2 Position => transform.position;

    public System.Action OnHooked;

    private int _wpIdx = 0;
    private SpriteRenderer _sr;

    // ================================================================
    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) _sr.color = normalColor;

        if (waypoints != null && waypoints.Count > 0)
            transform.position = new Vector3(
                waypoints[0].x, waypoints[0].y, transform.position.z);
    }

    void Update()
    {
        if (IsHooked || waypoints == null || waypoints.Count < 2) return;

        Vector2 cur = transform.position;
        Vector2 dest = waypoints[_wpIdx];
        float dist = Vector2.Distance(cur, dest);
        float step = moveSpeed * Time.deltaTime;

        if (step >= dist)
        {
            transform.position = new Vector3(dest.x, dest.y, transform.position.z);
            _wpIdx = (_wpIdx + 1) % waypoints.Count;
        }
        else
        {
            Vector2 dir = (dest - cur).normalized;
            Vector2 np = cur + dir * step;
            transform.position = new Vector3(np.x, np.y, transform.position.z);
        }
    }

    // ── 公开方法 ──────────────────────────────────────────────
    public void Hook()
    {
        if (IsHooked) return;
        IsHooked = true;
        if (_sr != null) _sr.color = hookedColor;
        OnHooked?.Invoke();
        Debug.Log("[LevelTarget] 被锚钩住！");
    }

    public void ResetTarget()
    {
        IsHooked = false;
        _wpIdx = 0;
        if (waypoints != null && waypoints.Count > 0)
            transform.position = new Vector3(
                waypoints[0].x, waypoints[0].y, transform.position.z);
        if (_sr != null) _sr.color = normalColor;
    }

    // ── Gizmos：路径可视化 ────────────────────────────────────
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count < 2) return;

        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.85f);
        for (int i = 0; i < waypoints.Count; i++)
        {
            Gizmos.DrawWireSphere(waypoints[i], 0.18f);
            int next = (i + 1) % waypoints.Count;
            Gizmos.DrawLine(waypoints[i], waypoints[next]);

            // 箭头中点
            Vector2 mid = (waypoints[i] + waypoints[next]) * 0.5f;
            Gizmos.DrawSphere(mid, 0.07f);
        }
    }
#endif
}