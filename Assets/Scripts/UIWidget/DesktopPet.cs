using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 把 CatWidget 变成桌面宠物：全屏透明覆盖、无边框、置顶，默认点击穿透，
/// 仅当鼠标落在可交互 UI（小猫 / 信箱 / 弹窗 / 按钮）上时临时接收鼠标。
/// 挂在 CatWidget 物体上，连好相机与 GraphicRaycaster。
/// </summary>
[DefaultExecutionOrder(-50)]
public class DesktopPet : MonoBehaviour
{
    [Tooltip("清屏相机，会被设为透明背景")]
    public Camera petCamera;
    [Tooltip("Canvas 上的 GraphicRaycaster，用于判断鼠标是否压在 UI 上")]
    public GraphicRaycaster raycaster;
    [Tooltip("窗口置顶")]
    public bool alwaysOnTop = true;
    [Tooltip("桌宠管理器：在“别处”点击鼠标时，通知它播放小猫 touch 动画（留空自动查找）")]
    public CatWidgetManager petManager;

    /// <summary>拖拽进行中标志（由 CatDragHandler 设置）。拖拽期间不切换穿透。</summary>
    public static bool DragActive;

    private EventSystem _es;
    private PointerEventData _ped;
    private readonly List<RaycastResult> _results = new List<RaycastResult>();
    private bool _clickThrough = true;   // 初始穿透
    private bool _prevMouseDown;         // 上一帧全局左键状态，用于检测“按下”的上升沿
    private float _quitTimer;            // 应急退出手势计时

    void Start()
    {
        // 相机透明清屏（配合 OS 层的窗口透明）。
        // 必须关掉 HDR 与 MSAA：它们会重写/丢弃 alpha 通道，导致背景变黑而非透明。
        if (petCamera != null)
        {
            petCamera.clearFlags = CameraClearFlags.SolidColor;
            petCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            petCamera.allowHDR = false;
            petCamera.allowMSAA = false;
        }

        _es = EventSystem.current;
        _ped = new PointerEventData(_es);

        // 桌宠失焦（点到别的软件）时 Unity 仍需运行，否则收不到“别处的点击”
        Application.runInBackground = true;

        if (petManager == null) petManager = FindObjectOfType<CatWidgetManager>();
        _prevMouseDown = TransparentWindow.IsAnyMouseDown();   // 避免启动瞬间误触发

#if !UNITY_EDITOR
        StartCoroutine(SetupWindow());
#endif
    }

    private IEnumerator SetupWindow()
    {
        // 等一帧，确保 Player 窗口与 D3D/Metal 交换链已经创建，再做透明/无边框处理
        yield return null;
        int w = Display.main.systemWidth;
        int h = Display.main.systemHeight;
        Screen.SetResolution(w, h, FullScreenMode.Windowed);

        // 反复应用：Unity（尤其 macOS）在启动头一秒会重建/重置窗口，
        // 单次设置会被覆盖，导致边框还在、背景变黑。这里在 ~1.5s 内多次重设。
        for (int i = 0; i < 15; i++)
        {
            yield return new WaitForSecondsRealtime(0.1f);
            TransparentWindow.Setup(w, h, alwaysOnTop);
        }
        TransparentWindow.SetClickThrough(true);
        _clickThrough = true;
    }

    void Update()
    {
        // —— 应急退出：同时按住鼠标左右键约 1.2 秒（或按住 Esc）→ 退出程序。
        //    用全局按键状态，即使窗口穿透/未聚焦也有效，防止桌宠卡住无法关闭。 ——
        if (TransparentWindow.IsBothMouseDown() || TransparentWindow.IsEscapeDown())
        {
            _quitTimer += Time.unscaledDeltaTime;
            if (_quitTimer > 1.2f)
            {
                Debug.Log("[DesktopPet] 应急退出触发");
                Application.Quit();
                return;
            }
        }
        else _quitTimer = 0f;

        if (raycaster == null) return;
        if (_es == null) { _es = EventSystem.current; if (_es == null) return; }
        if (_ped == null) _ped = new PointerEventData(_es);

        // —— 全局鼠标点击：在“别处”（不在小猫/本组件可交互区域）按下左键或右键时，播放小猫 touch 动画 ——
        // 用全局左/右键状态检测“按下”的上升沿；点在小猫/信箱等可交互 UI 上则不触发（点猫只冒爱心）。
        bool mouseDown = TransparentWindow.IsAnyMouseDown();
        if (mouseDown && !_prevMouseDown && !DragActive)
        {
            bool overSelf;
            if (!_clickThrough)
                overSelf = IsOverInteractive(Input.mousePosition);   // 窗口可交互：用引擎鼠标
            else
                overSelf = TransparentWindow.TryGetCursor(out Vector2 c) && IsOverInteractive(c);  // 穿透态：用全局光标
            if (!overSelf && petManager != null) petManager.PlayTouch();
        }
        _prevMouseDown = mouseDown;

        // 拖拽/按住期间绝不切换穿透，否则拖拽中途翻转会打断拖拽、让 EventSystem 卡死。
        // DragActive 来自 Unity 拖拽事件（可靠，不依赖原生）；再叠加系统级按键做兜底。
        if (DragActive || TransparentWindow.IsPrimaryMouseDown()) return;

        Vector2 probe;
        if (!_clickThrough)
            probe = Input.mousePosition;            // 可交互：引擎鼠标最准
        else if (!TransparentWindow.TryGetCursor(out probe))
            return;                                 // 穿透态：用系统全局光标

        // 命中任意 UI，或命中右上角“×”退出按钮区域 → 保持窗口可交互
        bool overUI = IsOverInteractive(probe) || InQuitButton(probe);

        bool wantClickThrough = !overUI;
        if (wantClickThrough != _clickThrough)
        {
            _clickThrough = wantClickThrough;
            TransparentWindow.SetClickThrough(_clickThrough);
        }
    }

    private bool IsOverInteractive(Vector2 screenPos)
    {
        _ped.position = screenPos;
        _results.Clear();
        raycaster.Raycast(_ped, _results);
        return _results.Count > 0;   // 命中任意 raycastTarget 图形即视为可交互区域
    }

    // ===== 右上角始终可用的“×”退出按钮（不依赖点击穿透是否正常，保证能关掉）=====
    const float QuitW = 40f, QuitH = 28f, QuitMargin = 8f;

    // 屏幕坐标（原点左下，与全局光标 / Input.mousePosition 一致）是否落在退出按钮上
    private bool InQuitButton(Vector2 sp)
    {
        return sp.x >= Screen.width - QuitW - QuitMargin && sp.x <= Screen.width - QuitMargin
            && sp.y >= Screen.height - QuitH - QuitMargin && sp.y <= Screen.height - QuitMargin;
    }

    void OnGUI()
    {
        // GUI 坐标原点在左上角
        var r = new Rect(Screen.width - QuitW - QuitMargin, QuitMargin, QuitW, QuitH);
        var prev = GUI.color;
        GUI.color = new Color(0.85f, 0.2f, 0.15f, 0.9f);
        if (GUI.Button(r, "×")) Application.Quit();
        GUI.color = prev;
    }
}
