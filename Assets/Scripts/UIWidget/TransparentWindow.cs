using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 把独立运行的程序窗口变成“透明、无边框、置顶、可切换点击穿透”的桌面宠物窗口。
/// Windows：user32/dwmapi 的经典 DWM 玻璃方案（DwmExtendFrameIntoClientArea 逐像素 alpha）。
///   注意：此方案要求【关闭】DXGI flip-model 交换链（Player Settings / ProjectSettings
///   useFlipModelSwapchain=0），否则背景会渲染成黑色；同时需 preserveFramebufferAlpha=1。
///   不用 WS_EX_LAYERED（与 GPU 交换链配合易黑屏）。
/// macOS：调用原生插件 TransparentWindowMac（见 Assets/Plugins/macOS/TransparentWindowMac.mm）。
/// 编辑器内所有方法为空操作，保持 Play 模式正常。
/// </summary>
public static class TransparentWindow
{
    private static int _screenW = 1920, _screenH = 1080;
    private static bool _ready;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int left, right, top, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS m);
    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_ESCAPE = 0x1B;

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_NOACTIVATE = 0x0010;

    private static IntPtr _hwnd;
    private static uint _exBase;

#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
    [DllImport("TransparentWindowMac")] private static extern void _SetupTransparentWindow([MarshalAs(UnmanagedType.I1)] bool topmost);
    [DllImport("TransparentWindowMac")] private static extern void _SetClickThrough([MarshalAs(UnmanagedType.I1)] bool enable);
    [DllImport("TransparentWindowMac")] private static extern void _GetGlobalMouse(out float x, out float y);
    [DllImport("TransparentWindowMac")] private static extern int _PressedMouseButtons();
    [DllImport("TransparentWindowMac")] private static extern void _ExitPetMode();

    private static bool _native = true;
    private static bool _warned = false;

    private static void MacFail()
    {
        _native = false;
        if (!_warned)
        {
            _warned = true;
            Debug.LogError("[TransparentWindow] 未找到/无法加载 TransparentWindowMac.bundle，" +
                "macOS 透明与点击穿透不可用。请在 Assets/Plugins/macOS 下编译该插件（见 桌面宠物说明.md）。");
        }
    }
#endif

    /// <summary>初始化透明窗口：铺满屏幕、无边框、（可选）置顶。可安全重复调用。</summary>
    public static void Setup(int width, int height, bool topmost)
    {
        _screenW = width; _screenH = height;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        _hwnd = GetActiveWindow();
        SetWindowLong(_hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);   // 无边框
        // 不用 WS_EX_LAYERED（与 flip-model 交换链冲突会黑屏）；透明靠下面的 DWM。
        _exBase = WS_EX_TOOLWINDOW | (topmost ? WS_EX_TOPMOST : 0);
        SetWindowLong(_hwnd, GWL_EXSTYLE, _exBase);
        var m = new MARGINS { left = -1, right = -1, top = -1, bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref m);   // 逐像素 alpha
        SetWindowPos(_hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, _screenW, _screenH,
                     SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native) { try { _SetupTransparentWindow(topmost); } catch (DllNotFoundException) { MacFail(); } catch (EntryPointNotFoundException) { MacFail(); } }
#endif
        _ready = true;
    }

    /// <summary>切换点击穿透：true=鼠标穿到后面的应用，false=本窗口接收鼠标。</summary>
    public static void SetClickThrough(bool enable)
    {
        if (!_ready) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        uint ex = _exBase | (enable ? WS_EX_TRANSPARENT : 0);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native) { try { _SetClickThrough(enable); } catch (DllNotFoundException) { MacFail(); } catch (EntryPointNotFoundException) { MacFail(); } }
#endif
    }

    /// <summary>退出桌宠模式：关闭穿透、取消置顶、恢复不透明，切回可交互的正常全屏窗口（进入关卡时调用）。</summary>
    public static void ExitPetMode()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (_hwnd == IntPtr.Zero) _hwnd = GetActiveWindow();
        SetWindowLong(_hwnd, GWL_EXSTYLE, 0);                 // 去掉 透明/置顶/工具窗
        var m = new MARGINS { left = 0, right = 0, top = 0, bottom = 0 };
        DwmExtendFrameIntoClientArea(_hwnd, ref m);           // 关闭 DWM 透明扩展 => 不透明
        SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, _screenW, _screenH,
                     SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native) { try { _ExitPetMode(); } catch (DllNotFoundException) { MacFail(); } catch (EntryPointNotFoundException) { MacFail(); } }
#endif
        _ready = false;   // 之后 SetClickThrough 变为空操作，防止再被切回穿透
    }

    /// <summary>全局左键是否按下（不依赖窗口焦点）。用于穿透切换的守卫，避免拖拽被打断。</summary>
    public static bool IsPrimaryMouseDown()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native)
        {
            try { return (_PressedMouseButtons() & 1) != 0; }
            catch (DllNotFoundException) { MacFail(); }
            catch (EntryPointNotFoundException) { MacFail(); }
        }
        return Input.GetMouseButton(0);
#else
        return Input.GetMouseButton(0);
#endif
    }

    /// <summary>全局左键或右键是否按下（不依赖窗口焦点）。用于“在别处点击”触发桌宠反应。</summary>
    public static bool IsAnyMouseDown()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
            || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native)
        {
            try { return (_PressedMouseButtons() & 3) != 0; }   // bit0=左 bit1=右
            catch (DllNotFoundException) { MacFail(); }
            catch (EntryPointNotFoundException) { MacFail(); }
        }
        return Input.GetMouseButton(0) || Input.GetMouseButton(1);
#else
        return Input.GetMouseButton(0) || Input.GetMouseButton(1);
#endif
    }

    /// <summary>全局左右键是否同时按下（应急退出手势用，不依赖窗口焦点）。</summary>
    public static bool IsBothMouseDown()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
            && (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native)
        {
            try { return (_PressedMouseButtons() & 3) == 3; }   // 左+右
            catch (DllNotFoundException) { MacFail(); }
            catch (EntryPointNotFoundException) { MacFail(); }
        }
        return Input.GetMouseButton(0) && Input.GetMouseButton(1);
#else
        return Input.GetMouseButton(0) && Input.GetMouseButton(1);
#endif
    }

    /// <summary>Esc 是否按下（Windows 用全局按键，其它平台用引擎输入）。</summary>
    public static bool IsEscapeDown()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        return (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
#else
        return Input.GetKey(KeyCode.Escape);
#endif
    }

    /// <summary>获取全局光标位置，转换为 Unity 屏幕坐标（原点左下）。穿透状态下仍然有效。</summary>
    public static bool TryGetCursor(out Vector2 pos)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (GetCursorPos(out POINT p)) { pos = new Vector2(p.X, _screenH - p.Y); return true; }
        pos = default; return false;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        if (_native)
        {
            try { _GetGlobalMouse(out float x, out float y); pos = new Vector2(x, y); return true; }
            catch (DllNotFoundException) { MacFail(); }
            catch (EntryPointNotFoundException) { MacFail(); }
        }
        pos = Input.mousePosition; return true;   // 插件不可用时退回引擎鼠标
#else
        pos = Input.mousePosition; return true;   // 编辑器
#endif
    }
}
