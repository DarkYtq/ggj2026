using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 船锚链条玩法（移植自 HTML 演示的自定义物理，不走 Unity 物理引擎）。
/// - 在玩家附近按住拖拽瞄准、松手发射锚头（拉拽反方向为初速方向，距离定力度）。
/// - 锚头受重力 + 空气阻力飞行；链长上限，到顶被拉住（只扣向外速度，保留切向）。
/// - 三种障碍（见 ChainObstacle）：普通=锚撞停/链绕折点；切链器=链碰即断；弹射器=锚碰弹飞。
/// - 物理用子步积分；几何用 segRect(线段-AABB) / circleRect(圆-AABB)。链条是折点链表。
///
/// 挂在玩家物体上即可：运行时会自动发现场景里的碰撞体当障碍、禁用旧的弹弓脚本、
/// 并生成一个切链器和一个弹射器示例。用 LineRenderer 画链与预测线，SpriteRenderer 画锚头。
/// </summary>
public class AnchorChain : MonoBehaviour
{
    [Header("发射原点（留空 = 本物体中心）")]
    public Transform origin;

    [Header("物理（世界单位 / 秒）")]
    public float gravity = 22f;           // 向下加速度大小
    [Range(0.01f, 1f)] public float dragPerSecond = 0.45f;   // 每秒保留的速度比例（空气阻力）
    public float chainMax = 10f;          // 链长上限
    public float launchMaxSpeed = 26f;    // 发射初速上限
    public float bounceSpeed = 18f;       // 弹射器弹飞速度
    public float anchorRadius = 0.22f;
    [Range(1, 8)] public int substeps = 3;
    public float settleSpeed = 0.4f;      // 到顶且低于此速判定静止

    [Header("瞄准")]
    public float grabRadius = 1.8f;       // 必须点在玩家附近才能拉
    public float launchSpeedPerUnit = 7f; // 拉拽距离(单位)→初速
    public float maxPull = 4f;
    public float previewSeconds = 1.4f;

    [Header("障碍识别")]
    public LayerMask obstacleMask = ~0;
    public bool spawnDemoObstacles = true;   // 自动生成切链器/弹射器示例

    [Header("外观")]
    public Color chainColor = new Color(0.6f, 0.67f, 0.73f);
    public Color anchorColor = new Color(0.24f, 0.30f, 0.38f);
    public Color aimColor = new Color(0.91f, 0.79f, 0.42f);

    // ================= 运行时 =================
    enum Phase { Idle, Aiming, Flying, Settled }
    Phase _phase = Phase.Idle;

    Camera _cam;
    Vector2 _aimStart, _aimMouse;
    Vector2 _pos, _vel;                       // 锚头位置/速度
    readonly List<Vector2> _nodes = new List<Vector2>();   // 链条折点（[0] 为链根）
    readonly HashSet<Collider2D> _hit = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> _launched = new HashSet<Collider2D>();
    bool _maxLen;

    class Obs { public Collider2D col; public ChainObstacle.Kind kind; public Vector2 dir; }
    readonly List<Obs> _obs = new List<Obs>();

    LineRenderer _chainLR, _aimLR, _trajLR;
    SpriteRenderer _anchorSR;
    Sprite _circleSprite, _squareSprite;

    Vector2 O => origin != null ? (Vector2)origin.position : (Vector2)transform.position;

    // ================= 初始化 =================
    void Awake()
    {
        _cam = Camera.main;
        if (origin == null) origin = transform;

        // 关掉旧的弹弓系统
        foreach (var t in FindObjectsOfType<PlayerThrower>()) t.enabled = false;
        foreach (var h in FindObjectsOfType<MeteorHammer>()) { h.enabled = false; h.gameObject.SetActive(false); }

        _squareSprite = MakeSquareSprite();
        _circleSprite = MakeCircleSprite(64);

        _chainLR = MakeLine("Chain", chainColor, 0.09f, 5);
        _aimLR = MakeLine("Aim", aimColor, 0.07f, 6);
        _trajLR = MakeLine("Traj", new Color(aimColor.r, aimColor.g, aimColor.b, 0.55f), 0.05f, 6);

        var a = new GameObject("Anchor");
        a.transform.SetParent(transform, false);
        _anchorSR = a.AddComponent<SpriteRenderer>();
        _anchorSR.sprite = _circleSprite;
        _anchorSR.color = anchorColor;
        _anchorSR.sortingOrder = 7;
        float d = anchorRadius * 2f;
        a.transform.localScale = new Vector3(d, d, 1f);   // circle 精灵直径=1 单位
        a.SetActive(false);

        CollectObstacles();
        if (spawnDemoObstacles) SpawnDemo();
    }

    void CollectObstacles()
    {
        _obs.Clear();
        foreach (var col in FindObjectsOfType<Collider2D>())
        {
            if (col.isTrigger) continue;
            if (((1 << col.gameObject.layer) & obstacleMask) == 0) continue;
            if (col.transform == origin || (origin != null && col.transform.IsChildOf(origin))) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
            if (col.GetComponent<MeteorHammer>() != null) continue;

            var co = col.GetComponent<ChainObstacle>();
            _obs.Add(new Obs
            {
                col = col,
                kind = co != null ? co.kind : ChainObstacle.Kind.Normal,
                dir = co != null ? co.launchDir.normalized : Vector2.up
            });
        }
    }

    void SpawnDemo()
    {
        bool hasCut = false, hasLaunch = false;
        foreach (var o in _obs)
        {
            if (o.kind == ChainObstacle.Kind.Cutter) hasCut = true;
            if (o.kind == ChainObstacle.Kind.Launcher) hasLaunch = true;
        }
        if (!hasCut)
            CreateObstacle("Cutter_Demo", new Vector2(0.5f, -0.6f), new Vector2(0.4f, 3.2f),
                           ChainObstacle.Kind.Cutter, Vector2.up, new Color(0.88f, 0.31f, 0.31f));
        if (!hasLaunch)
            CreateObstacle("Launcher_Demo", new Vector2(4f, -2.2f), new Vector2(1f, 1f),
                           ChainObstacle.Kind.Launcher, new Vector2(-0.5f, 1f), new Color(0.29f, 0.62f, 0.87f));
        CollectObstacles();
    }

    void CreateObstacle(string name, Vector2 pos, Vector2 size, ChainObstacle.Kind kind, Vector2 dir, Color color)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _squareSprite;
        sr.color = color;
        sr.sortingOrder = 3;
        go.AddComponent<BoxCollider2D>();   // 自动匹配 1x1 精灵，随 scale 变成 size
        var co = go.AddComponent<ChainObstacle>();
        co.kind = kind;
        co.launchDir = dir;
    }

    // ================= 输入 =================
    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        Vector2 mouse = _cam.ScreenToWorldPoint(Input.mousePosition);

        // 任意非瞄准状态下，点玩家附近即可（重新）拉弹
        if (_phase != Phase.Aiming && Input.GetMouseButtonDown(0) &&
            Vector2.Distance(mouse, O) <= grabRadius)
        {
            _phase = Phase.Aiming;
            _aimStart = O;
            _aimMouse = mouse;
            ClearShot();
        }

        if (_phase == Phase.Aiming)
        {
            _aimMouse = mouse;
            if (Input.GetMouseButtonUp(0)) ReleaseAim();
        }

        if (_phase == Phase.Flying) StepFrame();

        Draw();
    }

    void ReleaseAim()
    {
        Vector2 pull = _aimStart - _aimMouse;         // 拉拽反方向
        float mag = Mathf.Min(maxPull, pull.magnitude);
        float speed = Mathf.Min(launchMaxSpeed, mag * launchSpeedPerUnit);
        if (speed < 0.5f) { _phase = Phase.Idle; return; }
        Launch(pull.normalized, speed);
    }

    void Launch(Vector2 dir, float speed)
    {
        _pos = O;
        _vel = dir * speed;
        _nodes.Clear();
        _nodes.Add(O);
        _hit.Clear();
        _launched.Clear();
        _maxLen = false;
        _phase = Phase.Flying;
    }

    void ClearShot()
    {
        _nodes.Clear();
        _hit.Clear();
        _launched.Clear();
        _maxLen = false;
    }

    // ================= 物理 =================
    void StepFrame()
    {
        int ss = Mathf.Max(1, substeps);
        float dt = Time.deltaTime / ss;
        for (int s = 0; s < ss; s++) Substep(dt);

        KinkChain();
        CutChain();

        if (_vel.magnitude < settleSpeed && _maxLen)
        {
            _vel = Vector2.zero;
            _phase = Phase.Settled;
        }
    }

    void Substep(float dt)
    {
        _vel.y -= gravity * dt;
        _vel *= Mathf.Pow(dragPerSecond, dt);
        _pos += _vel * dt;

        // 链长上限：把锚夹到剩余链长的圆周上，只扣向外速度
        Vector2 last = _nodes[_nodes.Count - 1];
        float used = 0f;
        for (int i = 1; i < _nodes.Count; i++) used += Vector2.Distance(_nodes[i], _nodes[i - 1]);
        float remaining = chainMax - used;
        float tail = Vector2.Distance(_pos, last);
        if (remaining >= 0f && tail > remaining)
        {
            Vector2 dir = (_pos - last).normalized;
            _pos = last + dir * remaining;
            float vOut = Vector2.Dot(_vel, dir);
            if (vOut > 0f) _vel -= dir * vOut;
            _maxLen = true;
        }

        // 锚头碰撞
        for (int i = 0; i < _obs.Count; i++)
        {
            var o = _obs[i];
            if (o.col == null) continue;
            if (!CircleRect(_pos, anchorRadius, o.col.bounds, out Vector2 n, out float dep)) continue;

            _pos += n * dep;   // 顶出障碍
            if (o.kind == ChainObstacle.Kind.Launcher)
            {
                if (!_launched.Contains(o.col))
                {
                    _launched.Add(o.col);
                    _vel = o.dir * bounceSpeed;
                    _maxLen = false;
                }
            }
            else
            {
                if (!_hit.Contains(o.col)) { _hit.Add(o.col); _vel = Vector2.zero; }
                else { float vN = Vector2.Dot(_vel, n); if (vN < 0f) _vel -= n * vN; }
            }
        }
    }

    // 链条折点：末段与普通/弹射障碍相交则在交点插入折点（切链器不产生折点）
    void KinkChain()
    {
        Vector2 ln = _nodes[_nodes.Count - 1];
        float bestT = float.MaxValue;
        Vector2 bestPt = Vector2.zero;
        bool found = false;
        for (int i = 0; i < _obs.Count; i++)
        {
            var o = _obs[i];
            if (o.col == null || o.kind == ChainObstacle.Kind.Cutter) continue;
            if (SegRect(ln, _pos, o.col.bounds, out float t, out Vector2 pt) && t > 0.01f && t < bestT)
            {
                bestT = t; bestPt = pt; found = true;
            }
        }
        if (found && Vector2.Distance(ln, bestPt) > 0.05f) _nodes.Add(bestPt);
    }

    // 切链：任一链段穿过切链器 → 从该点断开，保留锚侧
    void CutChain()
    {
        for (int i = 0; i < _obs.Count; i++)
        {
            var o = _obs[i];
            if (o.col == null || o.kind != ChainObstacle.Kind.Cutter) continue;
            for (int s = 0; s < _nodes.Count; s++)
            {
                Vector2 A = _nodes[s];
                Vector2 B = (s == _nodes.Count - 1) ? _pos : _nodes[s + 1];
                if (SegRect(A, B, o.col.bounds, out float t, out Vector2 pt) && t > 0.01f)
                {
                    if (s > 0) _nodes.RemoveRange(0, s);
                    _nodes[0] = pt;
                    _maxLen = false;
                    return;
                }
            }
        }
    }

    // ================= 几何 =================
    static bool CircleRect(Vector2 c, float r, Bounds b, out Vector2 n, out float dep)
    {
        Vector2 mn = b.min, mx = b.max;
        float nx = Mathf.Clamp(c.x, mn.x, mx.x);
        float ny = Mathf.Clamp(c.y, mn.y, mx.y);
        Vector2 d = c - new Vector2(nx, ny);
        float dl = d.magnitude;
        if (dl >= r) { n = Vector2.zero; dep = 0f; return false; }
        if (dl < 1e-6f)
        {
            // 圆心在盒内：沿最近的边推出
            float[] deps = { c.x - mn.x, mx.x - c.x, c.y - mn.y, mx.y - c.y };
            Vector2[] ns = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
            int mi = 0;
            for (int i = 1; i < 4; i++) if (deps[i] < deps[mi]) mi = i;
            n = ns[mi]; dep = deps[mi] + r; return true;
        }
        n = d / dl; dep = r - dl; return true;
    }

    static bool SegRect(Vector2 a, Vector2 bpt, Bounds box, out float t, out Vector2 hit)
    {
        t = 0f; hit = Vector2.zero;
        float dx = bpt.x - a.x, dy = bpt.y - a.y;
        float tE = 0f, tL = 1f;
        if (!Slab(dx, a.x, box.min.x, box.max.x, ref tE, ref tL)) return false;
        if (!Slab(dy, a.y, box.min.y, box.max.y, ref tE, ref tL)) return false;
        if (tE < 0f || tE > 1f) return false;
        t = tE; hit = new Vector2(a.x + dx * tE, a.y + dy * tE); return true;
    }

    static bool Slab(float dv, float pv, float lo, float hi, ref float tE, ref float tL)
    {
        if (Mathf.Abs(dv) < 1e-9f) return pv >= lo && pv <= hi;
        float t1 = (lo - pv) / dv, t2 = (hi - pv) / dv;
        if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
        if (t1 > tE) tE = t1;
        if (t2 < tL) tL = t2;
        return tE <= tL + 1e-9f;
    }

    // ================= 绘制 =================
    void Draw()
    {
        // 链 + 锚头
        bool showChain = (_phase == Phase.Flying || _phase == Phase.Settled) && _nodes.Count > 0;
        if (showChain)
        {
            int n = _nodes.Count + 1;
            _chainLR.positionCount = n;
            for (int i = 0; i < _nodes.Count; i++) _chainLR.SetPosition(i, _nodes[i]);
            _chainLR.SetPosition(n - 1, _pos);
            _anchorSR.gameObject.SetActive(true);
            _anchorSR.transform.position = _pos;
        }
        else
        {
            _chainLR.positionCount = 0;
            _anchorSR.gameObject.SetActive(false);
        }

        // 瞄准：预测弹道 + 方向箭头
        if (_phase == Phase.Aiming)
        {
            Vector2 pull = _aimStart - _aimMouse;
            float mag = Mathf.Min(maxPull, pull.magnitude);
            float speed = Mathf.Min(launchMaxSpeed, mag * launchSpeedPerUnit);
            Vector2 dir = pull.sqrMagnitude > 1e-6f ? pull.normalized : Vector2.zero;
            DrawTrajectory(dir, speed);
            DrawAimArrow(dir, Mathf.Clamp01(speed / launchMaxSpeed));
        }
        else
        {
            _aimLR.positionCount = 0;
            _trajLR.positionCount = 0;
        }
    }

    void DrawTrajectory(Vector2 dir, float speed)
    {
        if (dir == Vector2.zero) { _trajLR.positionCount = 0; return; }
        var pts = new List<Vector3>();
        Vector2 p = O, v = dir * speed;
        pts.Add(p);
        float dt = 0.02f;
        int steps = Mathf.CeilToInt(previewSeconds / dt);
        for (int i = 0; i < steps; i++)
        {
            v.y -= gravity * dt;
            v *= Mathf.Pow(dragPerSecond, dt);
            p += v * dt;
            if (Vector2.Distance(p, O) > chainMax) break;
            bool hit = false;
            for (int k = 0; k < _obs.Count; k++)
                if (_obs[k].col != null && CircleRect(p, anchorRadius, _obs[k].col.bounds, out _, out _)) { hit = true; break; }
            if (hit) break;
            pts.Add(p);
        }
        _trajLR.positionCount = pts.Count;
        _trajLR.SetPositions(pts.ToArray());
    }

    void DrawAimArrow(Vector2 dir, float ratio)
    {
        if (dir == Vector2.zero) { _aimLR.positionCount = 0; return; }
        float len = 0.6f + ratio * 2.2f;
        Vector2 tip = O + dir * len;
        Vector2 back = tip - dir * 0.35f;
        Vector2 perp = new Vector2(-dir.y, dir.x) * 0.22f;
        _aimLR.positionCount = 5;
        _aimLR.SetPosition(0, O);
        _aimLR.SetPosition(1, tip);
        _aimLR.SetPosition(2, back + perp);
        _aimLR.SetPosition(3, tip);
        _aimLR.SetPosition(4, back - perp);
    }

    void OnGUI()
    {
        if (_phase != Phase.Aiming) return;
        Vector2 pull = _aimStart - _aimMouse;
        float mag = Mathf.Min(maxPull, pull.magnitude);
        float ratio = Mathf.Clamp01(mag * launchSpeedPerUnit / launchMaxSpeed);

        float x = 20, y = Screen.height - 40, w = 180, h = 16;
        var prev = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.HSVToRGB((1f - ratio) * 0.33f, 0.9f, 0.95f);
        GUI.DrawTexture(new Rect(x, y, w * ratio, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 20, 120, 20), "力度 " + Mathf.RoundToInt(ratio * 100) + "%");
        GUI.color = prev;
    }

    // ================= 工具 =================
    LineRenderer MakeLine(string name, Color color, float width, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        lr.sortingOrder = order;
        lr.positionCount = 0;
        return lr;
    }

    static Sprite MakeSquareSprite()
    {
        var t = new Texture2D(4, 4);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        t.SetPixels(px); t.Apply();
        return Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);   // 1 单位
    }

    static Sprite MakeCircleSprite(int size)
    {
        var t = new Texture2D(size, size);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                float a = Mathf.Clamp01(r - d);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);   // 直径 1 单位
    }
}
