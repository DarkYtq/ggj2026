using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 桌面待机 UI 小组件：小猫（待机走动/摆动、单击冒爱心、双击进关卡）、计时器（记录不在关卡中的时长）、
/// 信箱（每隔一段时间 +1 封、上限 n、点开弹窗看信、随机不重复文本、下一封/关闭）。
/// 组件已直接摆在 CatWidget 场景里，本脚本只做逻辑，不再运行时动态搭建 UI。
/// 在 Inspector 里把下面的引用连到场景中的对象即可。
/// </summary>
public class CatWidgetManager : MonoBehaviour
{
    [Header("信箱节奏")]
    [Tooltip("每隔多少秒 +1 封信")]
    public float letterIntervalSeconds = 10f;
    [Tooltip("信箱上限 n")]
    public int maxLetters = 5;

    [Header("关卡")]
    [Tooltip("双击小猫后加载的关卡场景名（需加入 Build Settings）")]
    public string levelSceneName = "SampleScene";
    public float fadeTime = 0.5f;

    [Header("交互")]
    public float doubleClickTime = 0.25f;

    [Header("信件文本池（数量建议 ≥ 上限 n，保证 n 封内不重复）")]
    [TextArea]
    public string[] letterTexts = {
        "锚定青山不放松，小猫正在偷吃中。",
        "两个黄鹂鸣翠柳，一只锚咪口水流。",
        "垂死病中惊坐起，泰山压顶是锚咪。",
        "谈笑无鸿儒，往来是白丁。不会调素琴，不爱阅金经。",
        "锚咪云：大陋特陋。",
        "老夫聊发锚咪狂，左抱咪，右亲咪，梦里尽是，锚咪的天堂。",
        "为报锚咪台上意，再窝囊，又何妨！",
        "千斤坠，锚咪藏，刀出鞘，回马枪；",
        "飞索现，拳脚忙，锚咪说，戏散场！",
        "天苍苍，野茫茫，锚咪想吃火腿肠。",
        "这盛世太大锚咪太渺小~藏在田野中无人来寻找~",
        "天不生无用之咪！海不长无名之锚！",
        "笑谈穷潦倒，锚咪还要吃多少——",
        "锚咪创业未半，蹦迪花光预算。",
        "春风又绿江南岸，锚咪坐拥麻辣拌。",
        "咪咪复咪咪，锚咪当户织。",
        "东市买奶茶，西市买泡面。",
        "南市买糖藕，北市买三鲜。",
        "巴山楚水凄凉地，人你不要抛下咪——",
        "锚咪横刀向天笑，笑完它就去睡觉。",
        "锚咪巧设连环计，从此本王不早朝。",
        "夜行的锚咪，绣衣的夜叉。",
        "脚下阵阵声，百鬼行路，锚咪一声令，猫猫大军镇山河！",
        "制作组嘲笑了你并让锚咪给你了一巴掌！"
    };

    [Header("场景引用（在 Inspector 中拖拽连接）")]
    [Tooltip("Canvas 根，用于统一给所有 Text 赋中文字体、以及作为爱心的父级备用")]
    public Canvas canvasRoot;
    [Tooltip("进关卡淡出用的 CanvasGroup（挂在 Root 上）")]
    public CanvasGroup group;
    [Tooltip("小猫所在的容器，爱心会生成在这里")]
    public RectTransform widget;
    public RectTransform cat;
    public ClickHandler catClick;
    public Text timerText;
    public Text countText;
    [Tooltip("信件弹窗正文文本")]
    public Text letterText;
    public Image envelopeIcon;
    public GameObject countBadge;
    public Button mailboxButton;
    public Button nextButton;
    public Button closeButton;
    public GameObject popup;
    [Tooltip("点击小猫冒出的爱心精灵")]
    public Sprite heartSprite;

    // 运行时状态
    private int _letters;
    private float _spawnTimer;
    private double _idleSeconds;
    private bool _inLevel;
    private const string SaveKey = "CatWidget.IdleSeconds";

    // 文本不重复用的牌堆
    private readonly List<int> _deck = new List<int>();
    private int _lastTextIndex = -1;

    void Start()
    {
        _idleSeconds = PlayerPrefs.GetFloat(SaveKey, 0f);

        WireUp();

        if (popup != null) popup.SetActive(false);
        UpdateMailboxUI();
        UpdateTimerText();
    }

    void Update()
    {
        if (!_inLevel)
        {
            _idleSeconds += Time.unscaledDeltaTime;

            if (_letters < maxLetters)
            {
                _spawnTimer += Time.unscaledDeltaTime;
                if (_spawnTimer >= letterIntervalSeconds)
                {
                    _spawnTimer -= letterIntervalSeconds;
                    _letters = Mathf.Min(maxLetters, _letters + 1);
                    UpdateMailboxUI();
                }
            }
            else _spawnTimer = 0f;   // 满了就不再累积
        }
        UpdateTimerText();
    }

    void OnDisable() { SaveTime(); }
    void OnApplicationQuit() { SaveTime(); }
    void SaveTime() { PlayerPrefs.SetFloat(SaveKey, (float)_idleSeconds); PlayerPrefs.Save(); }

    // ======================= 连线（不创建层级，只绑定回调 / 字体）=======================

    void WireUp()
    {
        // 场景里的 Text 用系统中文字体（占位字体无法显示中文），统一在运行时赋值
        if (canvasRoot != null)
            foreach (var t in canvasRoot.GetComponentsInChildren<Text>(true))
                t.font = UIShapes.CJKFont;

        if (mailboxButton != null) mailboxButton.onClick.AddListener(OpenPopup);
        if (nextButton != null) nextButton.onClick.AddListener(NextLetter);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePopup);

        if (catClick != null)
        {
            catClick.doubleClickTime = doubleClickTime;
            catClick.onSingle = OnCatSingleClick;
            catClick.onDouble = OnCatDoubleClick;
        }
    }

    // ======================= 交互逻辑 =======================

    void OnCatSingleClick()
    {
        for (int i = 0; i < 3; i++) SpawnHeart();
    }

    void OnCatDoubleClick()
    {
        StartCoroutine(EnterLevel());
    }

    void SpawnHeart()
    {
        if (widget == null || cat == null) return;

        var go = new GameObject("Heart", typeof(RectTransform));
        go.transform.SetParent(widget, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(52, 52);
        rt.anchoredPosition = cat.anchoredPosition +
            new Vector2(Random.Range(-40f, 40f), 190f + Random.Range(0f, 30f));

        var img = go.AddComponent<Image>();
        img.sprite = heartSprite;
        img.raycastTarget = false;
        go.AddComponent<FloatingHeart>();
    }

    IEnumerator EnterLevel()
    {
        _inLevel = true;          // 计时暂停
        SaveTime();
        if (popup != null) popup.SetActive(false);

        // 退出桌宠模式：停止穿透切换，窗口切回正常不透明可交互全屏，进游戏用
        var pet = FindObjectOfType<DesktopPet>();
        if (pet != null) pet.enabled = false;

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            if (group != null) group.alpha = Mathf.Clamp01(1f - t / fadeTime);
            yield return null;
        }
        if (group != null) group.alpha = 0f;

        if (!string.IsNullOrEmpty(levelSceneName))
        {
            if (Application.CanStreamedLevelBeLoaded(levelSceneName))
            {
                // 窗口切回正常全屏可交互不透明，再加载关卡
                TransparentWindow.ExitPetMode();
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                SceneManager.LoadScene(levelSceneName);
            }
            else
                Debug.LogWarning($"关卡场景 \"{levelSceneName}\" 未加入 Build Settings，无法加载。");
        }
    }

    // ---------- 信箱 / 弹窗 ----------

    void OpenPopup()
    {
        if (_letters <= 0) return;      // 没信不弹
        ReadOne();                      // 打开即已阅，信箱 -1
        if (popup != null) popup.SetActive(true);
        ShowLetter();
    }

    void NextLetter()
    {
        if (_letters <= 0) return;
        ReadOne();
        ShowLetter();
    }

    void ReadOne()
    {
        _letters = Mathf.Max(0, _letters - 1);
        UpdateMailboxUI();
    }

    void ShowLetter()
    {
        if (letterText != null) letterText.text = NextText();
        if (nextButton != null) nextButton.interactable = _letters > 0;   // 没有更多信就禁用下一封
    }

    void ClosePopup()
    {
        if (popup != null) popup.SetActive(false);
    }

    string NextText()
    {
        if (letterTexts == null || letterTexts.Length == 0) return "(空信件)";
        if (_deck.Count == 0) RebuildDeck();
        int idx = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);
        _lastTextIndex = idx;
        return letterTexts[idx];
    }

    void RebuildDeck()
    {
        _deck.Clear();
        for (int i = 0; i < letterTexts.Length; i++)
            if (i != _lastTextIndex || letterTexts.Length == 1) _deck.Add(i);
        for (int i = _deck.Count - 1; i > 0; i--)   // 洗牌
        {
            int j = Random.Range(0, i + 1);
            int tmp = _deck[i]; _deck[i] = _deck[j]; _deck[j] = tmp;
        }
    }

    void UpdateMailboxUI()
    {
        if (countText != null) countText.text = _letters.ToString();
        if (countBadge != null) countBadge.SetActive(_letters > 0);
        if (envelopeIcon != null) envelopeIcon.enabled = _letters > 0;
    }

    void UpdateTimerText()
    {
        if (timerText == null) return;
        int total = (int)_idleSeconds;
        int m = total / 60, s = total % 60;
        timerText.text = string.Format("计时器  {0:00}:{1:00}", m, s);
    }
}
