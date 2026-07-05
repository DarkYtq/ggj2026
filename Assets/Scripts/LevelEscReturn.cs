using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局“按 Esc 从关卡返回桌宠场景”的处理器，免挂载：
/// 游戏启动时用 RuntimeInitializeOnLoadMethod 自动生成一个常驻(DontDestroyOnLoad)对象，
/// 在任何“非桌宠场景”里按 Esc 即加载桌宠场景（Build Settings 第 0 个场景）。
/// 关卡场景无需挂任何组件即可生效。
/// </summary>
public static class LevelEscReturn
{
    // 回家目标：Build Settings 第 0 个场景（即桌宠 CatWidget 场景），改名也不影响
    private static string _homeScene = "CatWidget";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // 取 Build Settings 第 0 个场景名作为“家”，解析失败则保底用 CatWidget
        string path = SceneUtility.GetScenePathByBuildIndex(0);
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (!string.IsNullOrEmpty(name)) _homeScene = name;

        var go = new GameObject("~LevelEscReturn");
        go.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }

    public static string HomeScene => _homeScene;

    /// <summary>常驻组件：每帧检测 Esc。</summary>
    private class Runner : MonoBehaviour
    {
        // 进入关卡后，Windows 窗口是用 SWP_NOACTIVATE 显示的，可能没有键盘焦点，
        // 此时 Input.GetKeyDown(Esc) 收不到事件 → Esc 失效。改用全局按键状态
        // （TransparentWindow.IsEscapeDown：Windows 走 GetAsyncKeyState，不依赖焦点），
        // 并在这里自己做“按下上升沿”检测，等价于 GetKeyDown、且跨平台可用。
        private bool _prevEsc;
        private bool _loading;

        void Update()
        {
            bool esc = TransparentWindow.IsEscapeDown() || Input.GetKey(KeyCode.Escape);
            bool pressed = esc && !_prevEsc;   // 上升沿
            _prevEsc = esc;

            if (!pressed || _loading) return;

            string active = SceneManager.GetActiveScene().name;
            if (active == _homeScene) { _loading = false; return; }  // 已回到桌宠场景：解锁，退出交给 DesktopPet

            if (Application.CanStreamedLevelBeLoaded(_homeScene))
            {
                _loading = true;                              // 防止同一次切换里重复触发
                Debug.Log($"[LevelEscReturn] Esc → 返回桌宠场景 {_homeScene}");
                SceneManager.LoadScene(_homeScene);
            }
            else
            {
                Debug.LogWarning($"[LevelEscReturn] 桌宠场景 \"{_homeScene}\" 未加入 Build Settings。");
            }
        }
    }
}
