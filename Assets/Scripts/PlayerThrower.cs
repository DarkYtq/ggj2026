using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 弹弓式投掷流星锤。
/// 流程：左键按下开始后拉 -> 拖动鼠标（离锚点越远力度越大，显示皮筋 + 预测抛物线）
///       -> 松开左键，锤头朝“后拉反方向”丢出，受重力下坠、被链子拉住甩动。
/// 飞行中再次按下左键可立即收回重丢；超过 maxFlightTime 自动收回。
/// </summary>
public class PlayerThrower : MonoBehaviour
{
    [Header("引用（场景里拖入）")]
    public MeteorHammer hammer;       // 场景里的流星锤锤头
    public Transform anchor;          // 手的位置，留空则用本对象
    public LineRenderer bandLine;     // 皮筋，可选，自动创建

    [Header("弹弓力度")]
    public float maxPull = 4.5f;
    public float minSpeed = 10f;
    public float maxSpeed = 34f;
    public float startOffset = 0.8f;  // 预测起点离手的距离（避开玩家自身）

    [Header("拉拽区域限制（防止拉拽时锤头进地面/出界）")]
    public float groundTopY = -2.7f;  // 地面顶面
    public float hammerRadius = 0.25f;
    public float sideLimit = 7.4f;    // 左右可拉边界
    public float topLimit = 4.3f;     // 上方可拉边界

    [Header("轨迹预测")]
    public float previewSeconds = 1.6f;
    public float stepTime = 0.03f;

    [Header("回收")]
    public float maxFlightTime = 6f;

    [Header("力度配色（弱→强）")]
    public Color weakColor = new Color(0.6f, 0.9f, 1f);
    public Color strongColor = new Color(1f, 0.35f, 0.25f);

    enum State { Ready, Dragging, Flying }
    private State _state = State.Ready;
    private Camera _cam;
    private LineRenderer _traj;
    private Collider2D _col;
    private float _flightTimer;

    void Awake()
    {
        _cam = Camera.main;
        if (anchor == null) anchor = transform;
        _col = GetComponent<Collider2D>();

        // 预测线（渲染辅助）：取本对象上的 LineRenderer，没有就加一个
        _traj = GetComponent<LineRenderer>();
        if (_traj == null) _traj = gameObject.AddComponent<LineRenderer>();
        SetupLine(_traj, 0.08f, 10);

        // 皮筋：未指定则建子物体承载
        if (bandLine == null)
        {
            var b = new GameObject("Band");
            b.transform.SetParent(transform, false);
            bandLine = b.AddComponent<LineRenderer>();
        }
        SetupLine(bandLine, 0.12f, 3);
        bandLine.startColor = bandLine.endColor = new Color(0.85f, 0.7f, 0.45f);

        if (hammer != null)
        {
            hammer.SetAnchor(anchor);

            // 锤头会穿过手的位置飞出，忽略与玩家的碰撞
            var hc = hammer.GetComponent<Collider2D>();
            if (_col != null && hc != null) Physics2D.IgnoreCollision(hc, _col);

            hammer.Park(anchor.position);
        }
    }

    void SetupLine(LineRenderer lr, float width, int order)
    {
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        if (lr.sharedMaterial == null)
            lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.sortingOrder = order;
        lr.positionCount = 0;
    }

    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        Vector2 mouse = _cam.ScreenToWorldPoint(Input.mousePosition);

        switch (_state)
        {
            case State.Ready:
                // 必须点在玩家身上才能拉弹弓
                if (Input.GetMouseButtonDown(0) && OverPlayer(mouse)) _state = State.Dragging;
                break;

            case State.Dragging:
                Drag(mouse);
                if (Input.GetMouseButtonUp(0)) Release(mouse);
                break;

            case State.Flying:
                _flightTimer += Time.deltaTime;
                // 点玩家可提前收回并重新拉弹弓；否则超时自动收回
                if (Input.GetMouseButtonDown(0) && OverPlayer(mouse)) { Reload(); _state = State.Dragging; }
                else if (_flightTimer >= maxFlightTime) Reload();
                break;
        }
    }

    bool OverPlayer(Vector2 mouse)
    {
        return _col != null && _col.OverlapPoint(mouse);
    }

    // 计算锤头被拉到的合法位置：限制在 maxPull 内，且不进入地面/不越界
    Vector2 ParkPos(Vector2 mouse)
    {
        Vector2 a = anchor.position;
        Vector2 pull = mouse - a;
        if (pull.magnitude > maxPull) pull = pull.normalized * maxPull;
        Vector2 pos = a + pull;
        pos.y = Mathf.Max(pos.y, groundTopY + hammerRadius);   // 不低于地面
        pos.y = Mathf.Min(pos.y, topLimit);
        pos.x = Mathf.Clamp(pos.x, -sideLimit, sideLimit);
        return pos;
    }

    void Drag(Vector2 mouse)
    {
        Vector2 a = anchor.position;
        Vector2 pos = ParkPos(mouse);
        Vector2 pull = pos - a;
        if (hammer != null) hammer.Park(pos);   // 锤头跟着后拉（已限制在合法区域）

        DrawBand(a, pos);

        float t = Mathf.Clamp01(pull.magnitude / maxPull);
        float speed = Mathf.Lerp(minSpeed, maxSpeed, t);
        Vector2 dir = (-pull).normalized;
        DrawTrajectory(a, dir, speed, t, pull.magnitude > 0.05f);
    }

    void Release(Vector2 mouse)
    {
        _traj.positionCount = 0;
        if (bandLine != null) bandLine.positionCount = 0;

        Vector2 a = anchor.position;
        Vector2 pos = ParkPos(mouse);
        Vector2 pull = pos - a;
        if (pull.magnitude < 0.15f)
        {
            if (hammer != null) hammer.Park(anchor.position);
            _state = State.Ready;
            return;
        }

        float t = Mathf.Clamp01(pull.magnitude / maxPull);
        float speed = Mathf.Lerp(minSpeed, maxSpeed, t);
        Vector2 dir = (-pull).normalized;
        if (hammer != null) hammer.Launch(dir, speed);

        _flightTimer = 0f;
        _state = State.Flying;
    }

    void Reload()
    {
        if (hammer != null) hammer.Park(anchor.position);
        if (bandLine != null) bandLine.positionCount = 0;
        _state = State.Ready;
    }

    void DrawBand(Vector2 a, Vector2 pos)
    {
        if (bandLine == null) return;
        bandLine.positionCount = 2;
        bandLine.SetPosition(0, a);
        bandLine.SetPosition(1, pos);
    }

    /// <summary>预测：重力抛物线 + 链子最大长度约束</summary>
    void DrawTrajectory(Vector2 anchorPos, Vector2 dir, float speed, float t, bool show)
    {
        if (!show || dir == Vector2.zero || hammer == null) { _traj.positionCount = 0; return; }

        _traj.startColor = Color.Lerp(weakColor, strongColor, t);
        _traj.endColor = new Color(_traj.startColor.r, _traj.startColor.g, _traj.startColor.b, 0.15f);

        Vector2 g = Physics2D.gravity * hammer.FlyingGravity;
        float chain = hammer.ChainLength;
        float dt = Mathf.Max(0.005f, stepTime);
        int steps = Mathf.CeilToInt(previewSeconds / dt);

        var pts = new List<Vector3>();
        Vector2 pos = anchorPos + dir * startOffset;
        Vector2 vel = dir * speed;
        pts.Add(pos);

        for (int i = 0; i < steps; i++)
        {
            vel += g * dt;
            Vector2 next = pos + vel * dt;

            // 链长约束：超出则贴到以手为圆心、chain 为半径的圆上
            Vector2 fromA = next - anchorPos;
            if (fromA.magnitude > chain)
                next = anchorPos + fromA.normalized * chain;

            pos = next;
            pts.Add(pos);
        }

        _traj.positionCount = pts.Count;
        _traj.SetPositions(pts.ToArray());
    }
}
