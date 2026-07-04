using UnityEngine;
using UnityEngine.UI;

/// <summary>点击小猫时冒出的爱心：向上飘、放大后淡出，结束自毁。</summary>
public class FloatingHeart : MonoBehaviour
{
    public float life = 1.0f;
    public float riseSpeed = 90f;
    public float drift = 30f;

    private RectTransform _rt;
    private Image _img;
    private float _t;
    private float _driftDir;

    void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _img = GetComponent<Image>();
        _driftDir = Random.Range(-1f, 1f);
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = _t / life;

        _rt.anchoredPosition += new Vector2(_driftDir * drift, riseSpeed) * Time.unscaledDeltaTime;
        _rt.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.2f, Mathf.Min(1f, k * 2f));

        if (_img != null)
        {
            var c = _img.color;
            c.a = Mathf.Clamp01(1f - k);
            _img.color = c;
        }

        if (_t >= life) Destroy(gameObject);
    }
}
