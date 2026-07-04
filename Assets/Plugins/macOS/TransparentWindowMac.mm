// macOS 桌面宠物窗口原生插件（Cocoa + Metal）。
//
// 编译成【通用二进制】bundle（Apple Silicon + Intel 都能跑），在本文件所在目录执行：
//   clang -bundle -arch arm64 -arch x86_64 \
//         -framework Cocoa -framework QuartzCore -framework Metal \
//         -o TransparentWindowMac.bundle TransparentWindowMac.mm
//
// 产物 TransparentWindowMac.bundle 与本文件同目录。回到 Unity 选中该 bundle，
// Inspector 勾选 Standalone macOS，Apply。
//
// 透明要点：除了把窗口设为非不透明，还必须找到 Unity 渲染用的 CAMetalLayer，
// 把它的 opaque 设为 NO（否则背景恒为黑）。相机需以 alpha=0 清屏（DesktopPet 已做）。

#import <Cocoa/Cocoa.h>
#import <QuartzCore/QuartzCore.h>

// 递归查找 CAMetalLayer（Unity 的绘制层）
static CALayer* FindMetalLayer(CALayer* layer) {
    if (!layer) return nil;
    if ([NSStringFromClass([layer class]) containsString:@"CAMetalLayer"]) return layer;
    for (CALayer* sub in layer.sublayers) {
        CALayer* r = FindMetalLayer(sub);
        if (r) return r;
    }
    return nil;
}

static NSWindow* UnityWindow() {
    for (NSWindow* w in [NSApp windows]) {
        if ([w isVisible] && [w contentView] != nil) return w;
    }
    NSWindow* m = [NSApp mainWindow];
    if (m) return m;
    NSArray* ws = [NSApp windows];
    return ws.count ? [ws objectAtIndex:0] : nil;
}

static void ApplyTransparent(NSWindow* w, bool topmost) {
    if (!w) return;

    [w setStyleMask:NSWindowStyleMaskBorderless];
    [w setOpaque:NO];
    [w setBackgroundColor:[NSColor clearColor]];
    [w setHasShadow:NO];
    [w setTitlebarAppearsTransparent:YES];

    NSView* v = [w contentView];
    [v setWantsLayer:YES];
    CALayer* root = v.layer;
    if (root) {
        root.opaque = NO;
        root.backgroundColor = [[NSColor clearColor] CGColor];
    }
    // 关键：把 Unity 的 Metal 绘制层设为非不透明
    CALayer* ml = FindMetalLayer(root);
    if (ml) {
        ml.opaque = NO;
        ml.backgroundColor = [[NSColor clearColor] CGColor];
    }

    if (topmost) {
        [w setLevel:NSStatusWindowLevel];
        [w setCollectionBehavior:NSWindowCollectionBehaviorCanJoinAllSpaces |
                                 NSWindowCollectionBehaviorStationary |
                                 NSWindowCollectionBehaviorFullScreenAuxiliary];
    }

    NSRect f = [[NSScreen mainScreen] frame];
    [w setFrame:f display:YES];
}

extern "C" {

void _SetupTransparentWindow(bool topmost) {
    dispatch_async(dispatch_get_main_queue(), ^{
        ApplyTransparent(UnityWindow(), topmost);
    });
}

void _SetClickThrough(bool enable) {
    dispatch_async(dispatch_get_main_queue(), ^{
        NSWindow* w = UnityWindow();
        if (w) [w setIgnoresMouseEvents:enable];
    });
}

void _GetGlobalMouse(float* x, float* y) {
    NSPoint p = [NSEvent mouseLocation];   // 屏幕坐标，原点在左下，单位是“点”
    CGFloat s = [[NSScreen mainScreen] backingScaleFactor];   // Retina=2
    // 转成像素，与 Unity 的 Screen/GraphicRaycaster 坐标（像素）对齐
    if (x) *x = (float)(p.x * s);
    if (y) *y = (float)(p.y * s);
}

// 全局鼠标按键状态（不依赖窗口焦点）。bit0=左键。
int _PressedMouseButtons() {
    return (int)[NSEvent pressedMouseButtons];
}

// 退出桌宠模式：恢复成正常不透明、可交互、非置顶的全屏窗口（进关卡时调用）。
void _ExitPetMode() {
    dispatch_async(dispatch_get_main_queue(), ^{
        NSWindow* w = UnityWindow();
        if (!w) return;
        [w setIgnoresMouseEvents:NO];
        [w setOpaque:YES];
        [w setHasShadow:YES];
        [w setBackgroundColor:[NSColor blackColor]];
        [w setLevel:NSNormalWindowLevel];
        NSView* v = [w contentView];
        CALayer* root = v.layer;
        if (root) root.opaque = YES;
        CALayer* ml = FindMetalLayer(root);
        if (ml) ml.opaque = YES;
        NSRect f = [[NSScreen mainScreen] frame];
        [w setFrame:f display:YES];
    });
}

}
