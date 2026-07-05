#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// 一键把 CatWidget 打包成“桌面宠物”。打包前会强制写好所有保证透明/置顶/穿透的设置，
/// 避免出现黑底。菜单：Build > Desktop Pet ...；命令行：-executeMethod BuildDesktopPet.BuildWindows / BuildMac / BuildAll
/// </summary>
public static class BuildDesktopPet
{
    private const string ScenePath = "Assets/Scenes/CatWidget.unity";      // 开机场景（桌面宠物）
    // 双击进入后的关卡场景，顺序即过关顺序（按 build 序号 +1 推进）——共 10 关
    private static readonly string[] LevelScenes =
    {
        "Assets/Scenes/Level 1.unity",
        "Assets/Scenes/Level 2.unity",
        "Assets/Scenes/Level 3.unity",
        "Assets/Scenes/Level 4.unity",
        "Assets/Scenes/Level 5.unity",
        "Assets/Scenes/Level 6.unity",
        "Assets/Scenes/Level 7.unity",
        "Assets/Scenes/Level 8.unity",
        "Assets/Scenes/Level 9.unity",
        "Assets/Scenes/Level 10.unity",
    };
    // 打包场景列表 = 桌宠场景 + 全部关卡（避免手动重复，直接由上面拼出）
    private static string[] Scenes
    {
        get
        {
            var list = new System.Collections.Generic.List<string> { ScenePath };
            list.AddRange(LevelScenes);
            return list.ToArray();
        }
    }
    private const string OutRoot = "Builds";

    private const string ProductProcess = "CatPet";   // 产物可执行名（CatPet.exe / CatPet.app）

    [MenuItem("Build/Desktop Pet - Windows x64")]
    public static void BuildWindows()
    {
        KillRunningGame();    // 先结束正在运行的旧游戏，避免覆盖时文件被占用
        ApplySettings(BuildTarget.StandaloneWindows64);
        Run(BuildTarget.StandaloneWindows64, Path.Combine(OutRoot, "Windows", "CatPet.exe"));
    }

    [MenuItem("Build/Desktop Pet - macOS")]
    public static void BuildMac()
    {
        KillRunningGame();    // 先结束正在运行的旧游戏
        CompileMacPlugin();   // 确保原生透明插件已编译并被工程引用
        ApplySettings(BuildTarget.StandaloneOSX);
        Run(BuildTarget.StandaloneOSX, Path.Combine(OutRoot, "macOS", "CatPet.app"));
    }

    /// <summary>结束正在运行的已打包游戏进程（Windows / macOS 都处理）。</summary>
    public static void KillRunningGame()
    {
        try
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                RunSilent("taskkill", $"/F /T /IM {ProductProcess}.exe");
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                RunSilent("/usr/bin/killall", ProductProcess);          // .app 内可执行名
                RunSilent("/usr/bin/pkill", $"-f {ProductProcess}.app"); // 兜底：按路径匹配
            }
            System.Threading.Thread.Sleep(400);   // 等系统释放文件占用
            Debug.Log("[BuildDesktopPet] 已尝试结束旧游戏进程：" + ProductProcess);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BuildDesktopPet] 结束旧进程失败（可忽略）：" + e.Message);
        }
    }

    private static void RunSilent(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(psi)) { if (p != null) p.WaitForExit(2000); }
        }
        catch { /* 进程不存在 / 命令不可用等，忽略 */ }
    }

    private const string MacPluginDir = "Assets/Plugins/macOS";
    private const string MacPluginSrc = MacPluginDir + "/TransparentWindowMac.mm";
    private const string MacPluginBundle = MacPluginDir + "/TransparentWindowMac.bundle";

    /// <summary>用 clang 把 .mm 编成通用二进制 bundle，并设为 Standalone macOS 可用。仅在 macOS 编辑器上执行。</summary>
    public static void CompileMacPlugin()
    {
        if (Application.platform != RuntimePlatform.OSXEditor)
        {
            Debug.LogWarning("[BuildDesktopPet] 当前不在 macOS 编辑器上，跳过原生插件编译。" +
                "请在 Mac 上执行本步骤，否则 macOS 版没有透明/穿透。");
            return;
        }
        if (!File.Exists(MacPluginSrc))
        {
            Debug.LogError("[BuildDesktopPet] 找不到原生插件源码：" + MacPluginSrc);
            return;
        }

        // 源码比 bundle 新时才重编
        bool need = !File.Exists(MacPluginBundle) ||
                    File.GetLastWriteTimeUtc(MacPluginSrc) > File.GetLastWriteTimeUtc(MacPluginBundle);
        if (need)
        {
            var args = $"-bundle -arch arm64 -arch x86_64 -framework Cocoa -framework QuartzCore -framework Metal " +
                       $"-o \"{MacPluginBundle}\" \"{MacPluginSrc}\"";
            var psi = new ProcessStartInfo("/usr/bin/clang", args)
            {
                UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    Debug.LogError("[BuildDesktopPet] clang 编译原生插件失败（需已安装 Xcode Command Line Tools）：\n" + err + outp);
                    return;
                }
            }
            Debug.Log("[BuildDesktopPet] 已编译 " + MacPluginBundle);
        }

        // 导入并标记为 Standalone macOS 原生插件
        AssetDatabase.ImportAsset(MacPluginBundle, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(MacPluginBundle) as PluginImporter;
        if (imp != null)
        {
            imp.SetCompatibleWithAnyPlatform(false);
            imp.SetCompatibleWithEditor(false);
            imp.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
            imp.SaveAndReimport();
        }
    }

    [MenuItem("Build/Desktop Pet - Both")]
    public static void BuildAll()
    {
        BuildWindows();
        BuildMac();
    }

    /// <summary>把桌面宠物所需的 Player/图形设置强制写好。</summary>
    private static void ApplySettings(BuildTarget target)
    {
        // 窗口行为：窗口化（我们自己把窗口铺满并做透明），后台运行，不可全屏切换/缩放
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = true;
        PlayerSettings.visibleInBackground = true;
        PlayerSettings.resizableWindow = false;
        PlayerSettings.allowFullscreenSwitch = false;
        PlayerSettings.forceSingleInstance = true;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.captureSingleScreen = false;
        PlayerSettings.usePlayerLog = true;

        // 关键：保留帧缓冲 alpha。Windows 透明（DWM 逐像素 alpha）依赖它；
        // 若为 false，相机即使清屏 alpha=0，Unity 也会把背景写成不透明 → CatWidget 变黑底。
        // （macOS 走原生插件独立合成，不依赖此项，所以 Mac 一直是透明的。）
        PlayerSettings.preserveFramebufferAlpha = true;

        // 关键：图形 API。Windows 用 D3D11；macOS 用 Metal。
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        if (target == BuildTarget.StandaloneWindows64 || target == BuildTarget.StandaloneWindows)
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11 });
        else if (target == BuildTarget.StandaloneOSX)
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Metal });

        // 关键（修正）：必须【关闭】DXGI flip-model 交换链。
        // 本工程的 Windows 透明用的是 DwmExtendFrameIntoClientArea（经典 DWM 玻璃方案），
        // 该方案在 flip-model 下不生效、会把 CatWidget 背景渲染成黑色；
        // 切回 BitBlt(非 flip) 模型后，相机 alpha=0 的像素才会被 DWM 合成为透明。
        TrySetFlipModel(false);

        // 去掉开屏 Logo（个人版可能忽略，用 try 包住避免报错中断打包）
        try { PlayerSettings.SplashScreen.show = false; PlayerSettings.SplashScreen.showUnityLogo = false; }
        catch { /* 个人版不可关，忽略 */ }

        // 确保场景在构建列表且启用
        EnsureSceneInBuild();
        AssetDatabase.SaveAssets();
    }

    private static void TrySetFlipModel(bool enable)
    {
        try
        {
            var p = typeof(PlayerSettings).GetProperty("useFlipModelSwapchain",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p != null && p.CanWrite) p.SetValue(null, enable, null);
        }
        catch { /* 忽略：ProjectSettings 里已按需设置 */ }
    }

    private static void EnsureSceneInBuild()
    {
        // 直接把构建列表设为：CatWidget(0) + 各关卡，顺序 = 过关顺序
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        foreach (var lv in LevelScenes)
            list.Add(new EditorBuildSettingsScene(lv, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    private static void Run(BuildTarget target, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputPath,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary s = report.summary;
        Debug.Log($"[BuildDesktopPet] {target} => {s.result}  size={s.totalSize} bytes  out={outputPath}");

        if (s.result != BuildResult.Succeeded)
            throw new Exception($"[BuildDesktopPet] 打包失败：{target} -> {s.result}");
    }
}
#endif
