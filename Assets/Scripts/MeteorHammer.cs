using System.Collections;
using UnityEngine;

/// <summary>
/// 流星锤锤头。停靠时为 Kinematic、无重力；发射后变 Dynamic、受重力下坠。
/// 绳子约束用手动方式实现（FixedUpdate 里做“不可伸长绳索”）：超过绳长就把锤头夹回
/// 以手为圆心、绳长为半径的圆上，并只扣掉向外的速度、保留切向速度 —— 顺滑摆动、不会硬拽回弹。
/// 绳子用 LineRenderer 画成会下垂、可触地的绳索。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class MeteorHammer : MonoBehaviour
{
    [Header("绳子")]
    public float chainLength = 8f;      // 绳长（锤头离手的最大距离）
    public float flyingGravity = 1.5f;  // 飞行时的重力倍率（小一点能甩更高）

    [Header("绳子外观")]
    public int ropeSegments = 24;
    public float ropeWidth = 0.08f;
    public float sagFactor = 0.5f;      // 松弛时的下垂系数
    public float groundY = -2.7f;       // 地面高度，绳子最低垂到这里
    public Color ropeColor = new Color(0.55f, 0.45f, 0.3f);

    public bool IsFlying { get; private set; }
    public float ChainLength => chainLength;
    public float FlyingGravity => flyingGravity;

    private Rigidbody2D _rb;
    private LineRenderer _rope;
    private Transform _anchor;

    void Awake()
    {
        EnsureRefs();
        Park(transform.position);
        StartCoroutine(ConstrainAfterPhysics());
    }

    void EnsureRefs()
    {
        if (_rb == null)
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;  // 防高速穿透地面
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (_rope == null)
        {
            var c = new GameObject("Rope");
            c.transform.SetParent(transform, false);
            _rope = c.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.widthMultiplier = ropeWidth;
            _rope.numCapVertices = 2;
            _rope.numCornerVertices = 2;
            _rope.material = new Material(Shader.Find("Sprites/Default"));
            _rope.startColor = _rope.endColor = ropeColor;
            _rope.sortingOrder = 4;
            _rope.positionCount = 0;
        }
    }

    /// <summary>设置绳子另一端（玩家的手）</summary>
    public void SetAnchor(Transform t) { _anchor = t; }

    /// <summary>停靠：收回到手上，静止</summary>
    public void Park(Vector2 pos)
    {
        EnsureRefs();
        IsFlying = false;
        _rb.velocity = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.gravityScale = 0f;
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.position = pos;
        transform.position = pos;
        _rope.positionCount = 0;
    }

    /// <summary>丢出：给方向与力度，之后受重力 + 绳长约束</summary>
    public void Launch(Vector2 dir, float speed)
    {
        EnsureRefs();
        IsFlying = true;
        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.gravityScale = flyingGravity;
        _rb.velocity = dir.normalized * speed;
    }

    // 硬绳索约束：在物理步“之后”投影，避免逐帧漂移导致挣脱，且不回弹
    IEnumerator ConstrainAfterPhysics()
    {
        var wait = new WaitForFixedUpdate();
        while (true)
        {
            yield return wait;   // 此处运行在物理积分之后
            if (!IsFlying || _anchor == null) continue;

            Vector2 a = _anchor.position;
            Vector2 to = _rb.position - a;
            float d = to.magnitude;
            if (d > chainLength)
            {
                Vector2 n = to / d;
                _rb.position = a + n * chainLength;            // 夹回绳长圆周，锁死不超长
                float radial = Vector2.Dot(_rb.velocity, n);
                if (radial > 0f) _rb.velocity -= n * radial;    // 去掉向外分量，保留切向 -> 摆动、不回弹
            }
        }
    }

    void LateUpdate()
    {
        DrawRope();
    }

    void DrawRope()
    {
        if (!IsFlying || _anchor == null) { _rope.positionCount = 0; return; }

        Vector2 a = _anchor.position;
        Vector2 b = transform.position;
        float dist = Vector2.Distance(a, b);
        float slack = Mathf.Max(0f, chainLength - dist);
        float sag = slack * sagFactor;

        int n = Mathf.Max(2, ropeSegments);
        if (_rope.positionCount != n) _rope.positionCount = n;

        for (int i = 0; i < n; i++)
        {
            float f = i / (float)(n - 1);
            Vector2 p = Vector2.Lerp(a, b, f);
            p.y -= sag * 4f * f * (1f - f);
            if (p.y < groundY) p.y = groundY;
            _rope.SetPosition(i, new Vector3(p.x, p.y, 0f));
        }
    }
}
