# Unity 移动端构建约束

## 当前目标

本轮不在本机编译 iOS / Android，只固定以下内容：

1. Rust 插件 ABI。
2. Unity 插件目录结构。
3. 移动端产物形态。
4. 后续 GitHub Actions 的交叉编译输入约束。

## 统一前提

1. Unity C# 层统一走 `CreatePlayerPullRGBA` / `GetFrameMetaRGBA` / `CopyFrameRGBA`。
2. 移动端首版只要求 CPU RGBA 拉帧，不要求硬解和零拷贝。
3. 移动端只规划 `arm64`。

## Android

### 目标与产物

1. Rust target：`aarch64-linux-android`
2. 产物：`librustav_native.so`
3. Unity 插件目录：`Assets/Plugins/Android/arm64-v8a/librustav_native.so`

### GitHub Actions 现状

1. 已新增 Android `arm64-v8a` job。
2. GitHub Actions 使用 `ubuntu-latest + Android NDK + cargo-ndk`。
3. FFmpeg 不再依赖外部预编译包，改为通过 `mobile-ffmpeg-build -> ffmpeg-next/build` 在云端从源码构建。
4. 当前云端构建命令是：
   `cargo ndk -t arm64-v8a -o target/android-unity-libs build --release --lib --features mobile-ffmpeg-build`
5. 构建成功后会上传 `librustav_native.so` artifact。

### Unity 侧接入

1. 使用 `DllImport("rustav_native")`。
2. 创建播放器后调用 `Play(id)`。
3. 每帧轮询 `GetFrameMetaRGBA`，有帧时调用 `CopyFrameRGBA`。
4. 将 RGBA 数据写入 `Texture2D.LoadRawTextureData(...)` 后 `Apply()`。

## iOS

### 目标与产物

1. Rust target：
   - `aarch64-apple-ios`
   - `aarch64-apple-ios-sim`
2. 产物：`RustAV.xcframework`
3. Unity 插件目录：`Assets/Plugins/iOS/RustAV.xcframework`

### GitHub Actions 现状

1. 已新增 iOS `xcframework` job。
2. GitHub Actions 使用 `macos-latest + Xcode + rustup targets`。
3. FFmpeg 与 Android 一样，改为通过 `mobile-ffmpeg-build -> ffmpeg-next/build` 在云端从源码构建。
4. 当前云端构建命令是：
   - `cargo rustc --release --lib --locked --target aarch64-apple-ios --features mobile-ffmpeg-build -- --crate-type staticlib`
   - `cargo rustc --release --lib --locked --target aarch64-apple-ios-sim --features mobile-ffmpeg-build -- --crate-type staticlib`
5. 构建完成后通过 `xcodebuild -create-xcframework` 打包 `RustAV.xcframework` 并上传 artifact。

### Unity 侧接入

1. 保持与 Android 相同的 FFI 名称和调用顺序。
2. Unity C# 层仍走 RGBA 拉帧上传纹理。
3. 不额外依赖 D3D11 或 Windows 专属渲染回调。

## ABI 稳定面

以下接口已视为三端公共契约：

1. `CreatePlayerPullRGBA`
2. `Play`
3. `Stop`
4. `ReleasePlayer`
5. `GetFrameMetaRGBA`
6. `CopyFrameRGBA`

Windows 专属接口：

1. `GetPlayer(path, targetTexture)`
2. `UpdatePlayer(id)` 在 D3D11 纹理路径下用于主动写入纹理

## 后续最小落地顺序

1. 在 GitHub Actions 先稳定 Android `arm64-v8a`。
2. 再稳定 iOS device + simulator 两个 target。
3. 最后把生成物复制到 Unity 插件目录并做真机验证。

## 当前结论

1. Android 云端编译链路已经接入 GitHub Actions。
2. iOS 云端编译链路已经接入 GitHub Actions，并输出 `RustAV.xcframework`。
3. Windows 云端校验已经调整为只检查库本体，不再编译 `test_player`。
4. 三端当前都已接入缓存：
   - Windows：`rust-cache + vcpkg cache`
   - Android：`rust-cache`
   - iOS：`rust-cache`
5. GitHub Actions 当前目标已调整为：
   - Windows：构建 DLL，并打包依赖 DLL
   - Android：构建 `arm64-v8a` `.so`
   - iOS：构建设备静态库、simulator 静态库，并额外打包 `xcframework`
6. 最终会额外生成一个统一下载产物：
   `RustAV-UnityPlugins.zip`
7. 统一包布局说明见：
   `UNITY_PLUGIN_PACKAGE_LAYOUT.md`
