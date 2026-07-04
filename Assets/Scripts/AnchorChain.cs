using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public float anchorRadius = 0.11f;
    [Tooltip("命中目标的额外容差半径（越大越好钩中）")]
    public float hookRadius = 0.2f;
    [Range(1, 8)] public int substeps = 4;
    public float settleSpeed = 0.15f;

    [Header("瞄准")]
    public float grabRadius = 1.8f;
    public float launchSpeedPerUnit = 7f;
    public float maxPull = 4f;
    public float previewSeconds = 1.4f;

    [Header("障碍识别")]
    public LayerMask obstacleMask = ~0;

    [Header("过关（钩住目标并保持静止即胜利，自动切下一关场景）")]
    public bool advanceOnWin = true;
    public float winHoldTime = 1.5f;
    public float winStillSpeed = 0.25f;
    [Tooltip("过完最后一关后回到的 Build 场景序号（默认 1 = 第一关；0 通常是桌宠）")]
    public int loopToBuildIndex = 1;

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
    Collider2D _targetCol;         // 目标碰撞体（缓存，避免每子步 GetComponent）
    float _winTimer;
    bool _won;
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
        _targetCol = _target != null ? _target.GetComponent<Collider2D>() : null;
        Debug.Log($"[AnchorChain] 目标：{(_target != null ? _target.name : "无")}");
    }

    // ================= 输入 =================
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Application.Quit(); return; }   // 应急退出

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

        CheckWin();
        Draw();
    }

    // 钩住目标并保持静止 winHoldTime 秒 → 过关，加载下一关场景
    void CheckWin()
    {
        if (!advanceOnWin || _won || _target == null || _targetCol == null) return;
        if (!_target.IsHooked) { _winTimer = 0f; return; }

        // 用真实碰撞体形状判断锚头是否还在目标附近（和 Substep 里的 touching 逻辑保持一致）
        float pad = anchorRadius + hookRadius;
        bool stillNear = Vector2.Distance(_pos, _targetCol.ClosestPoint(_pos)) < pad;

        if (!stillNear)
        {
            _winTimer = 0f;
            _target.Unhook();
            return;
        }

        _winTimer += Time.deltaTime;
        if (_winTimer >= winHoldTime) Win();

        // 速度超过阈值 → 重置计时器，等静止后再开始倒计时
        if (_vel.magnitude > winStillSpeed)
        {
            _winTimer = 0f;
            return;
        }

        _winTimer += Time.deltaTime;
        if (_winTimer >= winHoldTime) Win();
    }

    void Win()
    {
        _won = true;
        Debug.Log("[AnchorChain] 🎉 过关！");
        int count = SceneManager.sceneCountInBuildSettings;
        int idx = SceneManager.GetActiveScene().buildIndex;
        if (idx < 0 || count <= 0) return;          // 场景没进 Build Settings，无法切换
        int next = idx + 1;
        if (next >= count) next = Mathf.Clamp(loopToBuildIndex, 0, count - 1);
        SceneManager.LoadScene(next);
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
        _winTimer = 0f;
        // 目标重置（关卡重开时）
        if (_target != null) _target.ResetTarget();
    }

    /// <summary>由 LevelManager 在关卡切换时调用</summary>
    public void ForceReset()
    {
        _phase = Phase.Idle;
        _won = false;
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
         
        }

        Vector2 startPos = _pos;   // 记录本子步起点，用于扫掠命中检测（防高速隧穿）

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
            if (!GetCircleColliderOverlap(_pos, anchorRadius, o.col, out Vector2 n, out float dep)) continue;
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

        /// 命中目标：扫掠检测 —— 把目标包围盒外扩(锚半径+容差)，再用本子步移动线段做相交，
        /// 这样即使锚头飞得快、两个采样点之间跨过目标，也能可靠判定为钩住（不再看角度/速度）。
        if (_target != null && !_target.IsHooked && _targetCol != null)
        {
            float pad = anchorRadius + hookRadius;

            bool swept = false;
            Vector2 sweepDir = _pos - startPos;
            float sweepLen = sweepDir.magnitude;
            if (sweepLen > 1e-6f)
            {
                int sc = Physics2D.RaycastNonAlloc(startPos, sweepDir / sweepLen, _rayBuf, sweepLen);
                for (int ri = 0; ri < sc; ri++)
                    if (_rayBuf[ri].collider == _targetCol) { swept = true; break; }
            }

            bool touching = Vector2.Distance(_pos, _targetCol.ClosestPoint(_pos)) < pad;

            if (swept || touching)
            {
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
            if (SegCollider(ln, _pos, o.col, out float t, out Vector2 pt) && t > 0.01f && t < bestT)
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
                if (SegCollider(A, B, o.col, out float t, out Vector2 pt) && t > 0.01f)
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

    static bool GetCircleColliderOverlap(Vector2 center, float radius, Collider2D col,
                                         out Vector2 normal, out float depth)
    {
        normal = Vector2.zero;
        depth = 0f;

        Vector2 closest = col.ClosestPoint(center);
        Vector2 diff = center - closest;
        float dist = diff.magnitude;

        if (dist < 1e-6f)
        {
            Bounds b = col.bounds;
            float[] ds = { center.x - b.min.x, b.max.x - center.x,
                            center.y - b.min.y, b.max.y - center.y };
            Vector2[] ns = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };
            int mi = 0;
            for (int i = 1; i < 4; i++) if (ds[i] < ds[mi]) mi = i;
            normal = ns[mi];
            depth = ds[mi] + radius;
            return true;
        }

        if (dist >= radius) return false;

        normal = diff / dist;
        depth = radius - dist;
        return true;
    }

    static readonly RaycastHit2D[] _rayBuf = new RaycastHit2D[16];

    static bool SegCollider(Vector2 a, Vector2 b, Collider2D col,
                            out float t, out Vector2 hit)
    {
        t = 0f;
        hit = Vector2.zero;
        Vector2 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-6f) return false;

        int cnt = Physics2D.RaycastNonAlloc(a, dir / len, _rayBuf, len);
        float bestT = float.MaxValue;
        bool found = false;

        for (int i = 0; i < cnt; i++)
        {
            if (_rayBuf[i].collider != col) continue;
            if (_rayBuf[i].fraction < 0.01f) continue;
            if (_rayBuf[i].fraction < bestT)
            {
                bestT = _rayBuf[i].fraction;
                hit = _rayBuf[i].point;
                found = true;
            }
        }

        if (found) { t = bestT; return true; }
        return false;
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
                if (_obs[k].col != null && GetCircleColliderOverlap(p, anchorRadius, _obs[k].col, out _, out _)) { hit = true; break; }
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
        var prev = GUI.color;

        // 过关倒计时条（钩住目标、保持静止累积中）
        if (advanceOnWin && !_won && _target != null && _target.IsHooked && _winTimer > 0f)
        {
            float p = Mathf.Clamp01(_winTimer / winHoldTime);
            float w = 220f, h = 18f, x = Screen.width * 0.5f - w * 0.5f, y = 28f;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.3f, 1f, 0.45f), Color.white, p);
            GUI.DrawTexture(new Rect(x, y, w * p, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y - 22, w, 20), $"🎯 保持住！{Mathf.CeilToInt(winHoldTime - _winTimer)} 秒后过关");
        }

        // 瞄准力度条
        if (_phase == Phase.Aiming)
        {
            Vector2 pull = _aimStart - _aimMouse;
            float mag = Mathf.Min(maxPull, pull.magnitude);
            float ratio = Mathf.Clamp01(mag * launchSpeedPerUnit / launchMaxSpeed);
            float x = 20, y = Screen.height - 40, w = 180, h = 16;
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.HSVToRGB((1f - ratio) * 0.33f, 0.9f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, w * ratio, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y - 20, 120, 20), "力度 " + Mathf.RoundToInt(ratio * 100) + "%");
        }

        GUI.color = prev;
    }


}
