# Unity 插件三端推进计划（Windows / iOS / Android）

> 项目：RustAV（Unity 插件）
> 更新日期：2026-03-06
> 当前范围：完成 Windows 主链路收口；iOS/Android 完成 ABI、接入约束和打包脚手架文档，后续交由 GitHub Actions 产出移动端包。

## 阶段状态

- [x] M1 架构清理（移除 SDL 主链路、平台解耦）
- [x] M2 统一 C ABI（播放器控制 + 帧导出）
- [x] M3 Android 接入准备（ABI 固化 + 插件目录约束 + 打包说明）
- [x] M4 iOS 接入准备（ABI 固化 + `xcframework` 约束 + 打包说明）
- [x] M5 Windows 收口（编译验证、文档、三端接口语义对齐）

## 范围定义（本轮完成项）

1. 支持协议：RTSP、RTMP、本地文件。
2. 支持编码：H264。
3. 支持模式：
   - Windows：Unity D3D11 纹理写入。
   - Windows / iOS / Android：CPU RGBA 拉帧导出。
4. 暂不做：音频、硬解、多路并发、复杂渲染路径。

## 交付结果

### M1：架构清理

1. 删除 SDL 主链路文件：
   - `src/SDLWindow.rs`
   - `src/Rendering/SDLWindowWriter.rs`
   - `examples/run_native_test_matrix.ps1`
2. 将 SDL 版 `examples/test_player.rs` 替换为 Win32/GDI 版可视化测试入口。
3. 移除 `Cargo.toml` 中的 `sdl2-sys` 依赖。
4. `TextureWriter` 改为平台分流：
   - Windows 下保留 D3D11 纹理写入。
   - 非 Windows 下不再尝试创建纹理 writer。
5. `lib.rs` 不再导出 SDL 模块，主库保持 Unity 插件定位。

### M2：统一 C ABI

已落地的导出接口：

1. `GetPlayer(path, targetTexture)`：Windows D3D11 纹理模式。
2. `CreatePlayerPullRGBA(path, targetWidth, targetHeight)`：跨平台 RGBA 拉帧模式。
3. `Play(id)` / `Stop(id)` / `ReleasePlayer(id)`。
4. `UpdatePlayer(id)`：Windows 纹理模式用于刷新写入；RGBA 拉帧模式可安全调用。
5. `GetFrameMetaRGBA(id, outMeta)`：返回宽高、stride、时间戳、帧序号。
6. `CopyFrameRGBA(id, dst, dstLen)`：复制最新 RGBA 帧。

新增内部组件：

1. `src/FrameExportClient.rs`：将解码后的 RGBA 帧复制到共享缓冲区。
2. `Player::CreateWithFrameExport(...)`：为移动端和通用 CPU 拉帧模式创建播放器。

### M3：Android 接入准备

本轮完成：

1. 固化 Android 侧推荐产物形态：`librustav_native.so`。
2. 固化 Unity 插件目录：`Assets/Plugins/Android/arm64-v8a/`。
3. 固化 Android 侧调用路径：使用 `CreatePlayerPullRGBA` + `GetFrameMetaRGBA` + `CopyFrameRGBA`。
4. 输出移动端构建说明文档，后续可直接迁移到 GitHub Actions。

本轮未执行：

1. 未在本机编译 Android target。
2. 未接入 Android FFmpeg 交叉链接。

### M4：iOS 接入准备

本轮完成：

1. 固化 iOS 侧推荐产物形态：`RustAV.xcframework`。
2. 固化目标组合：
   - `aarch64-apple-ios`
   - `aarch64-apple-ios-sim`
3. 固化 iOS 侧调用路径：使用 `CreatePlayerPullRGBA` + `GetFrameMetaRGBA` + `CopyFrameRGBA`。
4. 输出 `xcframework` 打包说明，后续可直接放入 GitHub Actions。

本轮未执行：

1. 未在本机编译 iOS target。
2. 未接入 iOS FFmpeg 交叉链接。

### M5：Windows 收口

1. `cargo check` 通过。
2. `cargo check --examples` 通过。
3. Win32/GDI 版 `test_player` 已可编译，并可用本地 sample file 拉起可视化窗口。
4. Windows CI 已从 SDL 链路切换为纯 FFmpeg 校验。
5. 三端 ABI 已统一为同一套播放器控制语义和 RGBA 拉帧语义。

## 核心文件

1. `src/UnityConnection.rs`
2. `src/Player.rs`
3. `src/FrameExportClient.rs`
4. `src/TextureClient.rs`
5. `src/Rendering/TextureWriter.rs`
6. `.github/workflows/rustav-matrix.yml`
7. `UNITY_PLUGIN_ABI.md`
8. `UNITY_MOBILE_BUILD.md`

## 变更日志

### 2026-03-06

1. 移除 SDL 依赖和相关示例，主库聚焦 Unity 插件。
2. 新增 Win32/GDI 版 `test_player`，用于 Windows 可视化测试。
3. 新增 CPU RGBA 拉帧导出链路和对应 C ABI。
4. 完成 Windows 构建验证。
5. 落地 iOS/Android 的插件打包规范和接入文档，等待 GitHub Actions 侧执行交叉编译。

## 当前结论

本轮 M1-M5 已按当前约束完成：

1. Windows：代码与示例可编译。
2. iOS/Android：接口、目录约束、产物形态和文档已固定；暂未本机编译。
