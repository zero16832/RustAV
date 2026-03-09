# RustAV 发布前检查清单

## 目标

把发布前的人工经验收敛成固定门槛，避免“本地看起来没问题就发布”。

## 必过项

默认假设公开仓与私有 `RustAV-Core` 同级放置。

1. `cargo check --manifest-path ../RustAV-Core/Cargo.toml --lib --examples --locked`
2. `cargo test --manifest-path ../RustAV-Core/Cargo.toml --lib --tests --locked`
3. `cargo check --manifest-path ../RustAV-Core/ios-staticlib/Cargo.toml --lib --locked`
4. `python scripts/ci/validate_ci_entrypoints.py --public-root . --core-root ../RustAV-Core`
5. `powershell -ExecutionPolicy Bypass -File scripts/qa/run_production_gate.ps1 -ProjectRoot . -CoreRoot ../RustAV-Core`
6. 若提供实时 AV 地址，`run_production_gate.ps1` 还应带上 `-RtspAvUri/-RtmpAvUri` 跑满 `run_av_soak.ps1`
7. 若提供 Unity 实时 AV 地址，`run_production_gate.ps1` 还应带上 `-UnityRtspUri/-UnityRtmpUri -UnitySeconds 600 -UnityAvSyncThresholdMs 200 -UnityAvSyncWarmupSampleCount 5`

## Native ABI

1. `RustAV_GetAbiVersion()` 返回与 `include/RustAV.h` 一致
2. `RustAV_GetBuildInfo()` 包含当前包版本与 ABI 版本
3. `RustAV_PlayerGetHealthSnapshot` 保持兼容
4. `RustAV_PlayerGetHealthSnapshotV2` 正常返回，并要求调用方初始化 `struct_size`
5. `RustAV_PlayerGetStreamInfo` 能返回主视频流原始宽高
6. `include/RustAV.h` 与实际导出函数保持一致

## 播放主链

1. 文件源 `audio_probe` 能稳定出视频和音频
2. RTSP `rtsp_probe` 能稳定出帧，且 `final_health` 输出正常
3. RTMP `rtmp_probe` 能稳定出帧，且 `final_health` 输出正常
4. `test_player` 在 Windows 上能正常出画和出声
5. 若提供 `mystream_av` 这类带音频实时流，`run_av_soak.ps1` 结束时 `final_health` 应满足 `timeouts=0 reconnects=0 vdrop=0 adrop=0`
6. `scripts/qa/run_unity_validation.ps1` 能完成 Unity 场景级文件/RTSP/RTMP 验证
7. Unity 场景级网络播放正式门槛：`ValidationSeconds=600`、`SkipFileCase`、`AvSyncWarmupSampleCount=5`、`av_sync_within_threshold=True` 且阈值为 `200ms`

## 同步与恢复

1. `final_health` 中 `video_frame_drop_count` / `audio_frame_drop_count` 不异常增长
2. `source_reconnect_count` 行为符合预期
3. 播放期间无持续 `underflow`
4. 音频输出层持续回写 `RustAV_PlayerSetAudioSinkDelaySeconds`

## 构建与打包

1. Windows 构建产物位于 `Assets/Plugins/x86_64`
2. Android 构建产物位于 `Assets/Plugins/Android/arm64-v8a`
3. iOS 构建产物位于 `Assets/Plugins/iOS` 和 `BuildSupport/iOS`
4. Unity 托管运行时源码位于 `Assets/UnityAV/UnityAV.Runtime.asmdef + Assets/UnityAV/Runtime`
5. Unity 示例工程内必须存在 `Assets/UnityAV/Editor/UnityAV.Editor.asmdef`
6. Unity 插件包不得再依赖预编译 `UnityAV.dll`
7. `RustAV-UnityPlugins.zip` 能由统一入口脚本组装
8. `UnityAVExample` 必须以内置工程存在于 `RustAV/UnityAVExample`
9. GitHub Actions 必须先构建 `UnityPlugins`，再注入 `UnityAVExample`，最后构建 Unity 示例程序
10. Release 资产必须至少包含：
   - `RustAV-UnityPlugins-v<version>.zip`
   - `RustAVExample-Windows64-v<version>.zip`
   - `RustAVExample-Android-v<version>.zip`
   - `RustAVExample-iOS-v<version>.zip`
11. CI 不再运行时修改 cargo registry 中的 `ffmpeg-sys-next`
12. Unity 场景级验证包默认窗口模式，并支持 `-windowWidth/-windowHeight` 显式覆盖

## 发布说明

1. 记录本次 ABI 版本
2. 记录本次 Unity 插件包名称
3. 记录本次 smoke/soak 结果
4. 记录已知限制和未覆盖风险
