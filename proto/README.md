# proto

这里用于存放协议相关“合同”文件，目标是让实现端（Windows/Android）都有**可对齐的唯一真相**，并可用测试向量做字节级回归。

当前协议定义见：

- `design.md` → “文档 2：协议规范（ABP/1.0）”

建议后续逐步补齐（WP0/WP1）：

- `abp-1.0.md`：独立的协议文档（从 design.md 抽出）
- `abp-1.0-control.schema.json`：控制消息 JSON schema（hello/welcome/ping/ptt/mute…）
- `abp-1.0-binary.md`：二进制帧头定义（字节布局、端序、seq/timestamp 规则）

