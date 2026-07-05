using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 胜利判定 + 关卡流转：
/// 1) 目标被钩住并保持静止 victoryHoldTime 秒 → 胜利。
/// 2) 胜利后先等所有（胜利）动画播放结束，再切到下一关场景。
/// 3) 下一关场景名 = 当前场景名末尾数字 +1（如 Level3 → Level4）。
/// 4) 在关卡场景中按 Esc 返回桌宠场景 CatWidget。
/// </summary>
public class VictoryManager : MonoBehaviour
{
    [Header("引用")]
    public AnchorChain anchorChain;

    [Header("胜利判定")]
    [SerializeField] private float victoryHoldTime = 3f;
    [SerializeField] private float stillSpeedThreshold = 0.08f;

    [Header("胜利动画（切场景前等这些播完）")]
    [Tooltip("要等待播放完毕的 Animator；留空则自动收集场景内所有 Animator")]
    public Animator[] victoryAnimators;
    [Tooltip("胜利时给上述 Animator 触发的 Trigger 名；留空则不触发、只等当前动画播完")]
    public string victoryTrigger = "";
    [Tooltip("动画播完后再额外等待的缓冲秒数")]
    [SerializeField] private float postAnimDelay = 0.2f;
    [Tooltip("等待动画的最长时间，防止循环动画导致永远切不了场景")]
    [SerializeField] private float maxAnimWait = 8f;

    [Header("切场景")]
    [Tooltip("桌宠（大厅）场景名，按 Esc 返回该场景")]
    public string catWidgetScene = "CatWidget";
    [Tooltip("没有下一关（下一关不在 Build Settings）时，返回桌宠场景")]
    public bool returnToCatWidgetIfNoNext = true;

    // ── 内部状态 ──────────────────────────────────────────────
    private LevelTarget _target;
    private float _holdTimer = 0f;
    private bool _isHolding = false;
    private bool _victoryTriggered = false;
    private bool _switching = false;

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
        // 注：按 Esc 返回桌宠场景由全局的 LevelEscReturn 处理（免挂载、任何关卡都生效）。
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

    // ── 胜利：先播/等动画，再切场景 ──────────────────────────
    void TriggerVictory()
    {
        if (_victoryTriggered) return;
        _victoryTriggered = true;
        AudioManager.PlayLevelClear();          // 过关音效
        Debug.Log("[VictoryManager] 🎉 胜利！等待动画结束后切换下一关。");
        StartCoroutine(VictoryRoutine());
    }

    IEnumerator VictoryRoutine()
    {
        Animator[] anims = (victoryAnimators != null && victoryAnimators.Length > 0)
            ? victoryAnimators
            : FindObjectsOfType<Animator>();

        // 触发胜利动画（若配置了 trigger）
        if (!string.IsNullOrEmpty(victoryTrigger))
            foreach (var a in anims)
                if (a != null && a.runtimeAnimatorController != null)
                    a.SetTrigger(victoryTrigger);

        yield return null;   // 等一帧，让 trigger 生效 / 进入过渡

        // 等待所有（非循环）动画播完，maxAnimWait 兜底防卡死
        float t = 0f;
        while (t < maxAnimWait && !AllAnimationsDone(anims))
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (postAnimDelay > 0f) yield return new WaitForSeconds(postAnimDelay);

        LoadNextLevel();
    }

    /// <summary>所有 Animator 都不在过渡中、且当前状态（非循环）已播到结尾，即视为播完。</summary>
    bool AllAnimationsDone(Animator[] anims)
    {
        if (anims == null) return true;
        foreach (var a in anims)
        {
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
            if (a.IsInTransition(0)) return false;
            var st = a.GetCurrentAnimatorStateInfo(0);
            if (st.loop) continue;                 // 循环动画永不结束，跳过
            if (st.normalizedTime < 1f) return false;
        }
        return true;
    }

    // ── 场景切换 ──────────────────────────────────────────────
    void LoadNextLevel()
    {
        if (_switching) return;
        string cur = SceneManager.GetActiveScene().name;
        string next = NextLevelName(cur);

        if (next != null && Application.CanStreamedLevelBeLoaded(next))
        {
            _switching = true;
            Debug.Log($"[VictoryManager] 切换关卡：{cur} → {next}");
            SceneManager.LoadScene(next);
            return;
        }

        Debug.LogWarning($"[VictoryManager] 下一关 \"{next}\" 不存在或未加入 Build Settings。");
        if (returnToCatWidgetIfNoNext) ReturnToCatWidget();
    }

    /// <summary>由当前场景名末尾数字 +1 得到下一关名，如 "Level3" → "Level4"。无数字则返回 null。</summary>
    string NextLevelName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        var m = Regex.Match(sceneName, @"^(.*?)(\d+)$");   // 前缀 + 末尾数字
        if (!m.Success) return null;
        if (int.TryParse(m.Groups[2].Value, out int n))
            return m.Groups[1].Value + (n + 1);            // 沿用当前场景前缀
        return null;
    }

    void ReturnToCatWidget()
    {
        if (_switching || string.IsNullOrEmpty(catWidgetScene)) return;
        if (Application.CanStreamedLevelBeLoaded(catWidgetScene))
        {
            _switching = true;
            Debug.Log($"[VictoryManager] 返回桌宠场景：{catWidgetScene}");
            SceneManager.LoadScene(catWidgetScene);
        }
        else
        {
            Debug.LogWarning($"[VictoryManager] 桌宠场景 \"{catWidgetScene}\" 未加入 Build Settings。");
        }
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
