using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局音频管理器（单例，跨场景常驻）。
/// - 关卡 BGM：进入任意 Level 场景自动播放同一首、循环；关卡之间切换不打断，连续不停。
///   离开关卡（回到桌宠场景 CatWidget 等）时停止 BGM。
/// - 音效：撸猫（cat_touch）、过关（level_clear），一次性播放。
///
/// 免挂载：靠 [RuntimeInitializeOnLoadMethod] 在游戏启动时自动创建，无需在任何场景里放物体。
/// 音频文件放在 Assets/Resources/Audio/ 下，按名字自动加载（可在 Inspector 覆盖）。
/// </summary>
[DisallowMultipleComponent]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("音频资源（留空则从 Resources/Audio/ 按名字自动加载）")]
    public AudioClip levelBgm;      // Resources/Audio/level_bgm
    public AudioClip catTouchSfx;   // Resources/Audio/cat_touch
    public AudioClip levelClearSfx; // Resources/Audio/level_clear

    [Header("音量")]
    [Range(0f, 1f)] public float musicVolume = 0.5f;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("BGM 生效的场景名包含（不区分大小写）")]
    [Tooltip("场景名里包含此关键字就播放关卡 BGM，例如 \"Level\"")]
    public string levelSceneKeyword = "level";

    private AudioSource _music;
    private AudioSource _sfx;

    // ── 启动时自动创建，无需任何场景挂载 ──────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[AudioManager]");
        go.AddComponent<AudioManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 自动加载缺省资源
        if (levelBgm == null)      levelBgm      = Resources.Load<AudioClip>("Audio/level_bgm");
        if (catTouchSfx == null)   catTouchSfx   = Resources.Load<AudioClip>("Audio/cat_touch");
        if (levelClearSfx == null) levelClearSfx = Resources.Load<AudioClip>("Audio/level_clear");

        _music = gameObject.AddComponent<AudioSource>();
        _music.loop = true;
        _music.playOnAwake = false;
        _music.clip = levelBgm;
        _music.volume = musicVolume;

        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.volume = sfxVolume;

        SceneManager.sceneLoaded += OnSceneLoaded;
        // 处理启动时已经加载的第一个场景
        ApplyBgmForScene(SceneManager.GetActiveScene().name);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyBgmForScene(scene.name);

    /// <summary>关卡场景 → 确保 BGM 在播（不打断已在播放的，实现连续不断）；否则停止。</summary>
    void ApplyBgmForScene(string sceneName)
    {
        bool isLevel = !string.IsNullOrEmpty(sceneName) && !string.IsNullOrEmpty(levelSceneKeyword)
                       && sceneName.ToLower().Contains(levelSceneKeyword.ToLower());

        if (isLevel)
        {
            _music.volume = musicVolume;
            if (levelBgm != null && (!_music.isPlaying || _music.clip != levelBgm))
            {
                _music.clip = levelBgm;
                _music.Play();   // 已在播则不会走到这里 → 关卡间无缝
            }
        }
        else
        {
            if (_music.isPlaying) _music.Stop();
        }
    }

    // ── 对外接口（静态便捷方法，任意脚本可直接调用）────────────
    public static void PlayCatTouch() { if (Instance != null) Instance.PlaySfx(Instance.catTouchSfx); }
    public static void PlayLevelClear() { if (Instance != null) Instance.PlaySfx(Instance.levelClearSfx); }

    public void PlaySfx(AudioClip clip)
    {
        if (clip == null || _sfx == null) return;
        _sfx.PlayOneShot(clip, sfxVolume);
    }
}
