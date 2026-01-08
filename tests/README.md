# tests

这里用于存放可重复的测试素材与测试向量，帮助你在改协议/改音频链路时不“凭感觉”回归。

建议后续逐步补齐（WP0/WP1/WP2/WP3）：

- `ProtocolTestVectors.json`：协议帧测试向量（header/payloadLen/seq 递增等）
- `audio/`：
  - `1khz_48k_mono_20ms.raw`：20ms 1kHz 测试帧（PCM16LE）
  - `1khz_48k_mono_10s.wav`：10 秒测试音（用于 Windows/Android 本地链路自检）

