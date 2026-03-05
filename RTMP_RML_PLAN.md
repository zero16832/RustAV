# RTMP rml_rtmp 接入计划（M1-M5）

> 目标：在 `RustAV` 中新增 `rtmp://` 拉流播放能力（基于 `rml_rtmp`），保持现有外部接口不变。
> 日期：2026-03-05

## 阶段状态

- [x] M1 计划落地与依赖接入
- [x] M2 `AVLibRTMPSource` 会话骨架（handshake/connect/play）
- [x] M3 RTMP 视频消息解析与 `AVLibPacket` 入队
- [x] M4 解码器构建与主播放链路接入
- [x] M5 验证与文档收口

## 执行约束

1. 先支持 `rtmp://` + H264（FLV Video CodecId=7）。
2. 不修改现有 FFI 接口与调用方式。
3. 非实时文件逻辑保持不变。

## 变更日志

- 2026-03-05 16:20：创建计划文件，进入 M1。
- 2026-03-05 17:14：完成 M2/M3。新增 `AVLibRTMPSource`，打通 TCP + RTMP handshake + client session（connect/play），实现 FLV AVC（H264）解析与 `AVLibPacket` 入队。
- 2026-03-05 17:16：完成 M4。`lib.rs`、`AVLibDecoder.rs`、`AVLibPlayer.rs`、`Player.rs` 已接入 `rtmp://` 分流；新增 `examples/rtmp_probe.rs`。
- 2026-03-05 17:28：完成 M5。`cargo check` 与 `cargo check --examples` 通过；`cargo run --example rtmp_probe -- rtmp://localhost:1935/mystream 10` 实测 10 秒累计 101 帧。
- 2026-03-05 17:43：补充复测。`cargo run --example rtmp_probe -- rtmp://localhost:1935/mystream 10` 实测 10 秒累计 41 帧，期间触发一次 socket 断开并自动重连恢复出帧。
