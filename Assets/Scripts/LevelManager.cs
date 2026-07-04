using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelManager : MonoBehaviour
{
    [Header("关卡根节点（按顺序拖入，每个根节点下放本关所有物体）")]
    public List<GameObject> levels = new List<GameObject>();

    [Header("过渡时长（秒）")]
    [SerializeField] private float shrinkDuration = 0.45f;
    [SerializeField] private float pauseDuration = 0.25f;
    [SerializeField] private float growDuration = 0.5f;

    [Header("引用")]
    public AnchorChain anchorChain;
    public VictoryManager victoryManager;

    // ── 内部状态 ──────────────────────────────────────────────
    private int _currentIndex = 0;
    private bool _transitioning = false;

    // 每个 Transform 的原始 localScale
    private readonly Dictionary<Transform, Vector3> _origScales
        = new Dictionary<Transform, Vector3>();

    // ================================================================
    void Start()
    {
        // 初始化：只激活第一关，其余全关
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] == null) continue;
            levels[i].SetActive(i == 0);
        }

        if (levels.Count > 0 && levels[0] != null)
        {
            RecordScales(levels[0]);
            SetScalesZero(levels[0]);
            StartCoroutine(GrowRoutine(levels[0]));
        }
    }

    // ── 供 VictoryManager 调用 ────────────────────────────────
    public void GoNextLevel()
    {
        if (_transitioning) return;
        StartCoroutine(TransitionRoutine());
    }

    // ================================================================
    IEnumerator TransitionRoutine()
    {
        _transitioning = true;

        // 2. 当前关缩小消失（锚链的重置放到激活下一关之后统一做，见第6步）
        GameObject cur = levels[_currentIndex];
        if (cur != null)
            yield return StartCoroutine(ShrinkRoutine(cur));

        yield return new WaitForSeconds(pauseDuration);

        // 3. 关闭当前关，恢复Scale（防止下次激活时为0）
        if (cur != null)
        {
            cur.SetActive(false);
            RestoreScales(cur);
        }

        // 4. 切换索引
        _currentIndex = (_currentIndex + 1) % levels.Count;
        GameObject next = levels[_currentIndex];

        if (next == null)
        {
            Debug.LogWarning("[LevelManager] 下一关为空！");
            _transitioning = false;
            yield break;
        }

        // 5. 激活下一关，Scale归零
        next.SetActive(true);
        RecordScales(next);
        SetScalesZero(next);

        // 6. 通知 AnchorChain 重新收集障碍物
        if (anchorChain != null)
            anchorChain.ForceReset();

        // 7. 通知 VictoryManager 重新寻找目标
        if (victoryManager != null)
            victoryManager.FindTarget();

        // 8. 下一关放大出现
        yield return StartCoroutine(GrowRoutine(next));

        _transitioning = false;
    }

    // ── 缩小：每个物体以自身原点为中心缩至0 ─────────────────
    IEnumerator ShrinkRoutine(GameObject root)
    {
        var trs = GetAll(root);
        var startScales = new Dictionary<Transform, Vector3>();
        foreach (var t in trs)
            if (t != null) startScales[t] = t.localScale;

        float elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float r = 1f - Mathf.SmoothStep(0f, 1f, elapsed / shrinkDuration);
            foreach (var t in trs)
            {
                if (t != null && startScales.ContainsKey(t))
                    t.localScale = startScales[t] * r;
            }
            yield return null;
        }

        foreach (var t in trs)
            if (t != null) t.localScale = Vector3.zero;
    }

    // ── 放大：每个物体从0长至原始Scale ──────────────────────
    IEnumerator GrowRoutine(GameObject root)
    {
        var trs = GetAll(root);

        float elapsed = 0f;
        while (elapsed < growDuration)
        {
            elapsed += Time.deltaTime;
            float r = Mathf.SmoothStep(0f, 1f, elapsed / growDuration);
            foreach (var t in trs)
            {
                if (t != null && _origScales.ContainsKey(t))
                    t.localScale = _origScales[t] * r;
            }
            yield return null;
        }

        foreach (var t in trs)
            if (t != null && _origScales.ContainsKey(t))
                t.localScale = _origScales[t];
    }

    // ── 工具 ──────────────────────────────────────────────────
    // 只操作关卡根节点：子物体跟随父级一起缩放，避免逐级相乘导致的“平方缩放”。
    List<Transform> GetAll(GameObject root)
    {
        return new List<Transform> { root.transform };
    }

    void RecordScales(GameObject root)
    {
        foreach (var t in GetAll(root))
            if (t != null && !_origScales.ContainsKey(t))
                _origScales[t] = t.localScale;
    }

    void RestoreScales(GameObject root)
    {
        foreach (var t in GetAll(root))
            if (t != null && _origScales.ContainsKey(t))
                t.localScale = _origScales[t];
    }

    void SetScalesZero(GameObject root)
    {
        foreach (var t in GetAll(root))
            if (t != null) t.localScale = Vector3.zero;
    }
}