# Unity 移动端构建约束

## 当前目标

本轮先固定三端统一 ABI 和构建输入，重点保证：

1. Windows 本地与云端可稳定编译
2. Android / iOS 云端可稳定编译
3. Unity 侧只依赖稳定的 `RustAV_*` 公共 ABI
4. Unity 托管桥接层以源码随插件包分发，不再依赖 `UnityAV.dll`
5. Unity 示例工程内部通过 `asmdef` 将 `Runtime` 与 `Editor` 隔离

## 三端统一 ABI

移动端和 Pull 路径统一依赖以下接口：

1. `RustAV_GetAbiVersion`
2. `RustAV_GetBuildInfo`
3. `RustAV_PlayerCreatePullRGBA`
4. `RustAV_PlayerRelease`
5. `RustAV_PlayerUpdate`
6. `RustAV_PlayerGetDuration`
7. `RustAV_PlayerGetTime`
8. `RustAV_PlayerPlay`
9. `RustAV_PlayerStop`
10. `RustAV_PlayerSeek`
11. `RustAV_PlayerSetLoop`
12. `RustAV_PlayerSetAudioSinkDelaySeconds`
13. `RustAV_PlayerGetHealthSnapshot`
14. `RustAV_PlayerGetHealthSnapshotV2`
15. `RustAV_PlayerGetFrameMetaRGBA`
16. `RustAV_PlayerCopyFrameRGBA`
17. `RustAV_PlayerGetAudioMetaPCM`
18. `RustAV_PlayerCopyAudioPCM`

Windows 纹理专属接口：

1. `RustAV_PlayerCreateTexture`
2. `RustAV_GetRenderEventFunc`
3. `UnityPluginLoad`
4. `UnityPluginUnload`

## Android

### 目标与产物

1. Rust target：`aarch64-linux-android`
2. 产物：`librustav_native.so`
3. Unity 插件目录：`Assets/Plugins/Android/arm64-v8a/librustav_native.so`
4. 托管源码目录：`Assets/UnityAV/UnityAV.Runtime.asmdef + Assets/UnityAV/Runtime/*.cs`

### 云端构建

1. GitHub Actions：`ubuntu-latest + Android NDK + cargo-ndk`
2. 当前通过 `mobile-ffmpeg-build` 在云端源码构建 FFmpeg
3. `ffmpeg-sys-next 8.0.1` 已固化到私有仓 `RustAV-Core/third_party/ffmpeg-sys-next-8.0.1`
4. 正式构建入口：
   `python3 scripts/ci/build_unity_plugins.py --public-root . --core-root ../RustAV-Core --platform android --output-root target/unity-package/android --cargo-ndk-output target/android-unity-libs --abi arm64-v8a`

## iOS

### 目标与产物

1. Rust target：
   - `aarch64-apple-ios`
   - `aarch64-apple-ios-sim`
2. 产物：`RustAV.xcframework`
3. Unity 插件目录：`Assets/Plugins/iOS/librustav_native.a + RustAV.h`
4. 附加构建支持目录：`BuildSupport/iOS/RustAV.xcframework`
5. 托管源码目录：`Assets/UnityAV/UnityAV.Runtime.asmdef + Assets/UnityAV/Runtime/*.cs`

### 云端构建

1. GitHub Actions：`macos-latest + Xcode + rustup targets`
2. 当前通过 `mobile-ffmpeg-build` 在云端源码构建 FFmpeg
3. iOS 使用私有 core 中的独立 manifest：`RustAV-Core/ios-staticlib/Cargo.toml`
4. `ffmpeg-sys-next 8.0.1` 已固化到私有仓 `RustAV-Core/third_party/ffmpeg-sys-next-8.0.1`
5. 正式构建入口：
   `python3 scripts/ci/build_unity_plugins.py --public-root . --core-root ../RustAV-Core --platform ios --manifest-path ios-staticlib/Cargo.toml --target-dir target/ios-staticlib --output-root target/unity-package/ios --xcframework-output target/apple-unity/RustAV.xcframework`
6. 构建结束后通过 `xcodebuild -create-xcframework` 打包，并将 `RustAV.xcframework` 放到 `BuildSupport/iOS`
7. iOS C# 侧统一使用 `DllImport("__Internal")`

## Windows

### 目标与产物

1. 产物：`rustav_native.dll`
2. Unity 插件目录：`Assets/Plugins/x86_64/rustav_native.dll`
3. 配套依赖 DLL 需放在同目录
4. 托管源码目录：`Assets/UnityAV/UnityAV.Runtime.asmdef + Assets/UnityAV/Runtime/*.cs`

### 当前要求

1. 云端只检查库本体，不编译 `test_player`
2. SDL 已经不再是 Unity 主链路依赖
3. Windows 纹理路径继续保留，Pull 路径与移动端保持 ABI 一致
4. 正式构建入口：
   `python scripts/ci/build_unity_plugins.py --public-root . --core-root ../RustAV-Core --platform windows --output-root target/unity-package/windows`

## 同步闭环要求

1. Native 以音频为主时钟
2. 宿主音频输出层应周期性调用 `RustAV_PlayerSetAudioSinkDelaySeconds`
3. Unity `MediaPlayerPull.cs` 已接入这套闭环
4. `test_player` 的 Windows 音频输出也已接入这套闭环
5. 若宿主需要区分“用户停播”和“自然播放结束”，应读取 `RustAV_PlayerGetHealthSnapshot.stop_reason`
6. 若宿主需要区分 source 的细分连接态，应优先读取 `RustAV_PlayerGetHealthSnapshotV2.source_connection_state`

## 当前工程结论

1. ABI 已统一为 `RustAV_*`
2. Unity C# 已完成静态绑定切换
3. Android / iOS / Windows 后续都以同一套头文件 `include/RustAV.h` 为准
4. 三端最终统一打包为 `RustAV-UnityPlugins.zip`
5. 本地 / CI / 发布构建统一通过 `scripts/ci/build_unity_plugins.py` 分发到各平台脚本
6. Unity 示例工程已内置到 `RustAV/UnityAVExample`，发布链路不再依赖外部 `UnityAV` 仓库
7. Unity 正式构建通过 `game-ci/unity-builder@v4 + UnityAV.Editor.RustAVReleaseBuild.BuildFromCi` 执行
8. 插件包中的 Unity 托管层改为源码分发，`UnityAV.dll` 不再是运行或发布必需项
9. `UnityAVExample/Assets/UnityAV` 当前按 `Runtime / Editor / Validation / Materials / Scenes` 分层
10. 公开仓只保留编排层，Rust 核心源码、examples、tests、第三方补丁位于私有仓 `RustAV-Core`
