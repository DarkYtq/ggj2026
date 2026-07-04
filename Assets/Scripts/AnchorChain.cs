using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 船锚链条玩法（移植自 HTML 演示的自定义物理，不走 Unity 物理引擎）。
/// - 在玩家附近按住拖拽瞄准、松手发射锚头（拉拽反方向为初速方向，距离定力度）。
/// - 锚头受重力 + 空气阻力飞行；链长上限，到顶被拉住（只扣向外速度，保留切向）。
/// - 三种障碍（见 ChainObstacle）：普通=锚撞停/链绕折点；切链器=链碰即断；弹射器=锚碰弹飞。
/// - 物理用子步积分；几何用 segRect(线段-AABB) / circleRect(圆-AABB)。链条是折点链表。
///
/// 所有可视物件（锚头、三条 LineRenderer、切链器/弹射器示例）都已摆在场景里，本脚本只引用不创建；
/// 障碍从场景现有碰撞体自动读入（带 ChainObstacle 的按其类型，其余按普通）。
/// </summary>
public class AnchorChain : MonoBehaviour
{
    [Header("发射原点（留空 = 本物体中心）")]
    public Transform origin;

    [Header("场景引用（在 Inspector 连好）")]
    public LineRenderer chainLine;      // 链条
    public LineRenderer aimLine;        // 瞄准方向箭头
    public LineRenderer trajLine;       // 预测弹道
    public SpriteRenderer anchorRenderer; // 锚头

    [Header("物理（世界单位 / 秒）")]
    public float gravity = 18f;
    [Range(0.01f, 1f)] public float dragPerSecond = 0.97f;
    public float chainMax = 10f;
    public float launchMaxSpeed = 26f;
    public float bounceSpeed = 18f;
    public float anchorRadius = 0.22f;
    [Range(1, 8)] public int substeps = 4;
    public float settleSpeed = 0.15f;

    [Header("瞄准")]
    public float grabRadius = 1.8f;
    public float launchSpeedPerUnit = 7f;
    public float maxPull = 4f;
    public float previewSeconds = 1.4f;

    [Header("障碍识别")]
    public LayerMask obstacleMask = ~0;

    [Header("外观")]
    public Color chainColor = new Color(0.6f, 0.67f, 0.73f);
    public Color anchorColor = new Color(0.24f, 0.30f, 0.38f);
    public Color aimColor = new Color(0.91f, 0.79f, 0.42f);

    // ================= 运行时 =================
    enum Phase { Idle, Aiming, Flying, Settled }
    Phase _phase = Phase.Idle;

    Camera _cam;
    Vector2 _aimStart, _aimMouse;
    Vector2 _pos, _vel;
    readonly List<Vector2> _nodes = new List<Vector2>();
    readonly HashSet<Collider2D> _hit = new HashSet<Collider2D>();
    readonly HashSet<Collider2D> _launched = new HashSet<Collider2D>();
    bool _maxLen;
    bool _chainCut;
    int _settleFrames;
    LevelTarget _target;           // ← 加这行
    public Vector2 AnchorVelocity => _vel;

    class Obs { public Collider2D col; public ChainObstacle.Kind kind; public Vector2 dir; }
    readonly List<Obs> _obs = new List<Obs>();

    Vector2 O => origin != null ? (Vector2)origin.position : (Vector2)transform.position;

    // ================= 初始化 =================
    void Awake()
    {
        _cam = Camera.main;
        if (origin == null) origin = transform;

        // 关掉旧的弹弓系统
        foreach (var t in FindObjectsOfType<PlayerThrower>()) t.enabled = false;
        foreach (var h in FindObjectsOfType<MeteorHammer>()) { h.enabled = false; h.gameObject.SetActive(false); }

        ConfigLine(chainLine, chainColor, 0.09f, 5);
        ConfigLine(aimLine, aimColor, 0.07f, 6);
        ConfigLine(trajLine, new Color(aimColor.r, aimColor.g, aimColor.b, 0.55f), 0.05f, 6);
        if (anchorRenderer != null)
        {
            anchorRenderer.color = anchorColor;
            anchorRenderer.gameObject.SetActive(false);
        }

        CollectObstacles();
        FindTarget();
    }

    void ConfigLine(LineRenderer lr, Color color, float width, int order)
    {
        if (lr == null) return;
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        lr.startColor = lr.endColor = color;
        lr.sortingOrder = order;
        lr.positionCount = 0;
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
    void FindTarget()
    {
        _target = FindObjectOfType<LevelTarget>();
        Debug.Log($"[AnchorChain] 目标：{(_target != null ? _target.name : "无")}");
    }

    // ================= 输入 =================
    void Update()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;
        Vector2 mouse = _cam.ScreenToWorldPoint(Input.mousePosition);

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
        Vector2 pull = _aimStart - _aimMouse;
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
        _chainCut = false;
        _settleFrames = 0;
        // 目标重置（关卡重开时）
        if (_target != null) _target.ResetTarget();
    }

    /// <summary>由 LevelManager 在关卡切换时调用</summary>
    public void ForceReset()
    {
        _phase = Phase.Idle;
        ClearShot();
        if (anchorRenderer != null) anchorRenderer.gameObject.SetActive(false);
        if (chainLine != null) chainLine.positionCount = 0;
        if (aimLine != null) aimLine.positionCount = 0;
        if (trajLine != null) trajLine.positionCount = 0;
        CollectObstacles();  // 重新扫描新关卡的障碍物
        FindTarget();        // 重新找目标
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
            _settleFrames++;
            if (_settleFrames > 40) // 连续40帧速度都很低才真正静止
            {
                _vel = Vector2.zero;
                _phase = Phase.Settled;
                _settleFrames = 0;
            }
        }
        else
        {
            _settleFrames = 0; // 速度恢复则重置计数
        }
    }

    void Substep(float dt)
    {
        // 0. 已钩住目标：把锚头钉在目标上、速度归零，跳过后续物理
        //    （目标被钩住后自身也停止移动，这样锚头保持静止，胜利倒计时才能累积）
        if (_target != null && _target.IsHooked)
        {
            _pos = _target.Position;
            _vel = Vector2.zero;
            return;
        }

        // 1. 重力 + 空气阻力
        _vel.y -= gravity * dt;
        _vel *= Mathf.Pow(dragPerSecond, dt);
        _pos += _vel * dt;

        // 2. 链条长度约束（圆弧摆动核心）
        // 2. 链条长度约束（切断后跳过，锚头自由飞行）
        if (!_chainCut)
        {
            Vector2 last = _nodes[_nodes.Count - 1];
            float used = 0f;
            for (int i = 1; i < _nodes.Count; i++)
                used += Vector2.Distance(_nodes[i], _nodes[i - 1]);

            float remaining = Mathf.Max(0f, chainMax - used);
            float tail = Vector2.Distance(_pos, last);

            if (tail > remaining)
            {
                Vector2 dir = (_pos - last).normalized;
                _pos = last + dir * remaining;
                float vOut = Vector2.Dot(_vel, dir);
                if (vOut > 0f) _vel -= dir * vOut;
                _maxLen = true;
            }
            else if (_maxLen)
            {
                Vector2 dir = tail > 0.001f
                    ? (_pos - last).normalized
                    : Vector2.down;
                _pos = last + dir * remaining;
                float vRadial = Vector2.Dot(_vel, dir);
                _vel -= dir * vRadial;
            }
        }

        // 3. 锚头与障碍物碰撞（原代码不变）
        for (int i = 0; i < _obs.Count; i++)
        {
            var o = _obs[i];
            if (o.col == null) continue;
            if (!CircleRect(_pos, anchorRadius, o.col.bounds, out Vector2 n, out float dep)) continue;

            _pos += n * dep;

            if (o.kind == ChainObstacle.Kind.Launcher)
            {
                if (!_launched.Contains(o.col))
                {
                    _launched.Add(o.col);
                    _vel = o.dir * bounceSpeed;
                    _maxLen = false;  // 弹射后重置，允许链条重新延伸
                }
            }
            else
            {
                if (!_hit.Contains(o.col))
                {
                    _hit.Add(o.col);
                    _vel = Vector2.zero;
                }
                else
                {
                    float vN = Vector2.Dot(_vel, n);
                    if (vN < 0f) _vel -= n * vN;
                }
            }
        }

        if (_target != null && !_target.IsHooked)
        {
            var tcol = _target.GetComponent<Collider2D>();
            if (tcol != null && CircleRect(_pos, anchorRadius, tcol.bounds, out Vector2 tn, out float tdep))
            {
                _pos += tn * tdep;
                _vel = Vector2.zero;
                _target.Hook();
            }
        }

    }

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
                    _chainCut = true;
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
        bool showChain = (_phase == Phase.Flying || _phase == Phase.Settled)
                         && _nodes.Count > 0
                         && !_chainCut; // ← 切断后不显示链条和锚头        if (chainLine != null)
        {
            if (showChain)
            {
                int n = _nodes.Count + 1;
                chainLine.positionCount = n;
                for (int i = 0; i < _nodes.Count; i++) chainLine.SetPosition(i, _nodes[i]);
                chainLine.SetPosition(n - 1, _pos);
            }
            else chainLine.positionCount = 0;
        }
        if (anchorRenderer != null)
        {
            // 锚头单独判断：飞行或静止时始终显示，与链条是否被切断无关
            bool showAnchor = (_phase == Phase.Flying || _phase == Phase.Settled);
            anchorRenderer.gameObject.SetActive(showAnchor);
            if (showAnchor) anchorRenderer.transform.position = _pos;
        }

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
            if (aimLine != null) aimLine.positionCount = 0;
            if (trajLine != null) trajLine.positionCount = 0;
        }
    }

    void DrawTrajectory(Vector2 dir, float speed)
    {
        if (trajLine == null) return;
        if (dir == Vector2.zero) { trajLine.positionCount = 0; return; }
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
        trajLine.positionCount = pts.Count;
        trajLine.SetPositions(pts.ToArray());
    }

    void DrawAimArrow(Vector2 dir, float ratio)
    {
        if (aimLine == null) return;
        if (dir == Vector2.zero) { aimLine.positionCount = 0; return; }
        float len = 0.6f + ratio * 2.2f;
        Vector2 tip = O + dir * len;
        Vector2 back = tip - dir * 0.35f;
        Vector2 perp = new Vector2(-dir.y, dir.x) * 0.22f;
        aimLine.positionCount = 5;
        aimLine.SetPosition(0, O);
        aimLine.SetPosition(1, tip);
        aimLine.SetPosition(2, back + perp);
        aimLine.SetPosition(3, tip);
        aimLine.SetPosition(4, back - perp);
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
}
