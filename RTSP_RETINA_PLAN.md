# RTSP Retina 接入计划（M1-M5）

> 目标：在 `RustAV` 中以 `retina` 替代现有 RTSP 拉流路径（不涉及 LIVE555），并保持现有对外接口不变。
> 时间：2026-03-05

## 阶段状态

- [x] M1 计划落地与依赖接入
- [x] M2 `AVLibRTSPSource` 会话骨架（retina）
- [x] M3 `VideoFrame -> AVLibPacket` 转换与入队
- [x] M4 参数更新、解码器构建与稳定性处理
- [x] M5 回归验证与文档收口

## M1 任务拆解

1. 创建本地计划文件并记录阶段状态。
2. 增加 `retina`、`tokio`（及流式迭代所需依赖）到 `Cargo.toml`。
3. 执行 `cargo check` 验证可编译。

## 变更日志

- 2026-03-05 15:30：创建计划文件，进入 M1。
- 2026-03-05 15:36：完成 M1。已引入 `retina/tokio/futures-util/url` 并通过 `cargo check`。
- 2026-03-05 15:45：完成 M2/M3。`AVLibRTSPSource` 已切换为 `retina` 会话流程（DESCRIBE/SETUP/PLAY/DEMUX）。
- 2026-03-05 15:47：完成 M4。新增 codec 参数缓存与 ffmpeg decoder 构建；`AVLibVideoDecoder` 支持动态重建 `SwsContext`。
- 2026-03-05 15:48：M5 进行中。已通过 `cargo check` 和 `cargo check --examples`；待 RTSP 实流联调。
- 2026-03-05 16:08：完成 M5。使用 `rtsp://localhost:8554/mystream` 联调 10 秒，累计解码 122 帧；观察到配置识别 `h264 640x360`，解码缩放路径正常。
