using UnityEngine;

/// <summary>
/// 小猫待机动画：左右缓慢走动 + 轻微上下起伏 + 摆尾式的轻微旋转摇摆（占位用变换动画表现）。
/// </summary>
public class CatIdleAnim : MonoBehaviour
{
    public float walkRange = 40f;     // 左右走动幅度(像素)
    public float walkSpeed = 0.6f;    // 走动速度
    public float bobHeight = 6f;      // 上下起伏
    public float swayAngle = 5f;      // 摇摆角度(度)
    public float swaySpeed = 2.5f;

    private RectTransform _rt;
    private Vector2 _home;

    void Start()
    {
        _rt = GetComponent<RectTransform>();
        _home = _rt.anchoredPosition;
    }

    void Update()
    {
        if (_rt == null) return;
        float tt = Time.unscaledTime;
        float x = Mathf.Sin(tt * walkSpeed) * walkRange;
        float y = Mathf.Abs(Mathf.Sin(tt * walkSpeed * 2f)) * bobHeight;
        _rt.anchoredPosition = _home + new Vector2(x, y);
        _rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(tt * swaySpeed) * swayAngle);

        // 走动方向翻转朝向
        float face = Mathf.Cos(tt * walkSpeed);
        var s = _rt.localScale;
        s.x = Mathf.Abs(s.x) * (face >= 0 ? 1f : -1f);
        _rt.localScale = s;
    }
}
