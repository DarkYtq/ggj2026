#!/usr/bin/env bash
# 一键把 CatWidget 打包成桌面宠物（命令行 / CI 用）。
#
# 用法：
#   ./build_desktop_pet.sh            # 默认打两个平台（Windows + macOS）
#   ./build_desktop_pet.sh win        # 只打 Windows
#   ./build_desktop_pet.sh mac        # 只打 macOS
#   UNITY_PATH=/path/to/Unity ./build_desktop_pet.sh   # 手动指定 Unity 可执行文件
#
# 前置：跨平台打包需在 Unity Hub 里装好对应的 Build Support 模块
#   （Windows Build Support / Mac Build Support）。
#   macOS 还需先编译 Assets/Plugins/macOS/TransparentWindowMac.bundle（见 桌面宠物说明.md）。

set -euo pipefail

PROJECT="$(cd "$(dirname "$0")" && pwd)"
VERSION="2022.3.62f3"

# 定位 Unity 可执行文件
if [[ -n "${UNITY_PATH:-}" ]]; then
  UNITY="$UNITY_PATH"
elif [[ "$(uname)" == "Darwin" ]]; then
  UNITY="/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity"
else
  UNITY="$HOME/Unity/Hub/Editor/${VERSION}/Editor/Unity"
fi

if [[ ! -x "$UNITY" ]]; then
  echo "找不到 Unity 可执行文件：$UNITY"
  echo "请用 UNITY_PATH 环境变量指定，例如："
  echo "  UNITY_PATH='/Applications/Unity/Hub/Editor/${VERSION}/Unity.app/Contents/MacOS/Unity' $0 $*"
  exit 1
fi

case "${1:-both}" in
  win)  METHOD="BuildDesktopPet.BuildWindows" ;;
  mac)  METHOD="BuildDesktopPet.BuildMac" ;;
  both) METHOD="BuildDesktopPet.BuildAll" ;;
  *)    echo "未知参数：$1（可选 win / mac / both）"; exit 1 ;;
esac

# 先结束正在运行的旧游戏，避免覆盖 .app/.exe 时文件被占用
echo "结束旧游戏进程 CatPet（若在运行）……"
if [[ "$(uname)" == "Darwin" || "$(uname)" == "Linux" ]]; then
  killall CatPet 2>/dev/null || true
  pkill -f "CatPet.app" 2>/dev/null || true
fi

mkdir -p "$PROJECT/Builds"
LOG="$PROJECT/Builds/build.log"

echo "Unity:   $UNITY"
echo "Project: $PROJECT"
echo "Method:  $METHOD"
echo "打包中……日志：$LOG"

"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJECT" \
  -executeMethod "$METHOD" \
  -logFile "$LOG"

echo "完成，产物在 $PROJECT/Builds/"
