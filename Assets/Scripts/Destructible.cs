using UnityEngine;

/// <summary>
/// 可摧毁物体（木箱）。被正在飞行的流星锤锤头砸中时销毁自己，并迸出一堆碎块作为破碎效果。
/// 碎块沿用自身精灵的小块，带重力四散飞开，短暂存在后自动消失。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class Destructible : MonoBehaviour
{
    [Header("破碎效果")]
    public int fragmentCount = 10;      // 碎块数量
    public float burstSpeed = 5f;       // 迸射速度
    public float fragmentGravity = 1.5f;
    public float fragmentLife = 1.5f;   // 碎块存活时间
    public Vector2 fragmentScale = new Vector2(0.15f, 0.32f); // 碎块相对原体缩放范围

    private bool _broken;

    void OnCollisionEnter2D(Collision2D c)
    {
        var mh = c.collider.GetComponent<MeteorHammer>();
        if (mh != null && mh.IsFlying) Shatter();
    }

    void Shatter()
    {
        if (_broken) return;
        _broken = true;

        var sr = GetComponent<SpriteRenderer>();
        Sprite sp = sr != null ? sr.sprite : null;
        Color col = sr != null ? sr.color : Color.white;
        int order = sr != null ? sr.sortingOrder : 0;
        Vector3 basePos = transform.position;
        float baseScale = transform.localScale.x;

        for (int i = 0; i < fragmentCount; i++)
        {
            var g = new GameObject("Fragment");
            g.transform.position = basePos + (Vector3)(Random.insideUnitCircle * 0.35f * baseScale);
            g.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            float s = baseScale * Random.Range(fragmentScale.x, fragmentScale.y);
            g.transform.localScale = new Vector3(s, s, 1f);

            var fsr = g.AddComponent<SpriteRenderer>();
            fsr.sprite = sp;
            fsr.color = col * Random.Range(0.8f, 1f);
            fsr.sortingOrder = order + 1;

            var rb = g.AddComponent<Rigidbody2D>();
            rb.gravityScale = fragmentGravity;
            Vector2 dir = Random.insideUnitCircle.normalized;
            rb.velocity = dir * Random.Range(burstSpeed * 0.4f, burstSpeed) + Vector2.up * burstSpeed * 0.3f;
            rb.angularVelocity = Random.Range(-540f, 540f);

            var bc = g.AddComponent<BoxCollider2D>();
            bc.size = Vector2.one;

            Destroy(g, fragmentLife + Random.Range(0f, 0.4f));
        }

        Destroy(gameObject);
    }
}
