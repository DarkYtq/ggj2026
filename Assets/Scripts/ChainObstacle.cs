using UnityEngine;

/// <summary>
/// 锚链障碍标记：贴在带 Collider2D 的物体上，告诉锚链系统这块 AABB 是哪种障碍。
///   Normal   普通障碍：锚撞上清零速度、链绕过去产生折点
///   Cutter   切链器：链碰到就从该点断开
///   Launcher 弹射器：锚碰到按 launchDir 方向弹飞
/// 不加此组件的普通碰撞体会被当作 Normal 处理。
/// </summary>
[DisallowMultipleComponent]
public class ChainObstacle : MonoBehaviour
{
    public enum Kind { Normal, Cutter, Launcher }

    public Kind kind = Kind.Normal;

    [Tooltip("弹射器：锚碰到后被弹飞的方向（会自动归一化）")]
    public Vector2 launchDir = Vector2.up;

    /// <summary>世界空间轴对齐包围盒（AABB），优先用碰撞体，其次渲染器，最后用 transform。</summary>
    public Bounds WorldBounds
    {
        get
        {
            var col = GetComponent<Collider2D>();
            if (col != null) return col.bounds;
            var rend = GetComponent<Renderer>();
            if (rend != null) return rend.bounds;
            return new Bounds(transform.position, transform.lossyScale);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (kind != Kind.Launcher) return;
        Gizmos.color = Color.cyan;
        Vector3 c = WorldBounds.center;
        Vector3 d = ((Vector3)launchDir.normalized) * 0.9f;
        Gizmos.DrawLine(c, c + d);
        Gizmos.DrawSphere(c + d, 0.08f);
    }
}
