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

    /// <summary>拖拽进行中标志（由 CatDragHandler 设置）。拖拽期间不切换穿透。</summary>
    public static bool DragActive;

    private EventSystem _es;
    private PointerEventData _ped;
    private readonly List<RaycastResult> _results = new List<RaycastResult>();
    private bool _clickThrough = true;   // 初始穿透

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
        if (raycaster == null) return;
        if (_es == null) { _es = EventSystem.current; if (_es == null) return; }
        if (_ped == null) _ped = new PointerEventData(_es);

        // 拖拽/按住期间绝不切换穿透，否则拖拽中途翻转会打断拖拽、让 EventSystem 卡死。
        // DragActive 来自 Unity 拖拽事件（可靠，不依赖原生）；再叠加系统级按键做兜底。
        if (DragActive || TransparentWindow.IsPrimaryMouseDown()) return;

        bool overUI;
        if (!_clickThrough)
        {
            // 已可交互：窗口能收到鼠标，用引擎自身的 Input.mousePosition 判定（像素级准确，
            // 避免因全局光标坐标偏差而误判成“没压在 UI 上”→ 错误开穿透）。
            overUI = IsOverInteractive(Input.mousePosition);
        }
        else
        {
            // 穿透态：引擎收不到鼠标，只能用系统全局光标判断是否该恢复交互。
            if (!TransparentWindow.TryGetCursor(out Vector2 cursor)) return;
            overUI = IsOverInteractive(cursor);
        }

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
}
