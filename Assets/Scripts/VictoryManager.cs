using UnityEngine;

public class VictoryManager : MonoBehaviour
{
    [Header("引用")]
    public AnchorChain anchorChain;

    [Header("胜利参数")]
    [SerializeField] private float victoryHoldTime = 3f;
    [SerializeField] private float stillSpeedThreshold = 0.08f;

    // ── 内部状态 ──────────────────────────────────────────────
    private LevelTarget _target;
    private float _holdTimer = 0f;
    private bool _isHolding = false;
    private bool _victoryTriggered = false;

    public float HoldProgress => Mathf.Clamp01(_holdTimer / victoryHoldTime);

    // ================================================================
    void Start() => FindTarget();

    public void FindTarget()
    {
        _target = FindObjectOfType<LevelTarget>();
        _holdTimer = 0f;
        _isHolding = false;
        _victoryTriggered = false;
        Debug.Log($"[VictoryManager] 找到目标：{(_target != null ? _target.name : "无")}");
    }

    void Update()
    {
        if (_victoryTriggered || _target == null || anchorChain == null) return;

        // 目标未被钩住 → 重置计时
        if (!_target.IsHooked) { ResetTimer(); return; }

        // 检查锚头速度
        bool isStill = anchorChain.AnchorVelocity.magnitude < stillSpeedThreshold;

        if (isStill)
        {
            _isHolding = true;
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= victoryHoldTime) TriggerVictory();
        }
        else
        {
            ResetTimer();
        }
    }

    void ResetTimer()
    {
        _holdTimer = 0f;
        _isHolding = false;
    }

    void TriggerVictory()
    {
        if (_victoryTriggered) return;
        _victoryTriggered = true;
        Debug.Log("[VictoryManager] 🎉 胜利！切换下一关。");

        var lm = FindObjectOfType<LevelManager>();
        if (lm != null) lm.GoNextLevel();
    }

    // ── OnGUI：胜利倒计时条 ───────────────────────────────────
    void OnGUI()
    {
        if (!_isHolding || _victoryTriggered) return;

        float progress = HoldProgress;
        float cx = Screen.width * 0.5f;
        float w = 220f, h = 18f;
        float x = cx - w * 0.5f, y = 28f;

        var prev = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);

        GUI.color = Color.Lerp(new Color(0.3f, 1f, 0.45f), Color.white, progress);
        GUI.DrawTexture(new Rect(x, y, w * progress, h), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 24, w, 22),
            $"🎯 保持静止！{Mathf.CeilToInt(victoryHoldTime - _holdTimer)} 秒后胜利");

        GUI.color = prev;
    }
}