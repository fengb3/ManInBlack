# 飞书适配器

> 本文档是 CLAUDE.md 的子文档，Agent 在修改 Feishu Adaptor 相关代码前应先阅读此文档。

## 概述

飞书 IM bot via WebSocket + streaming cards. Flow: `FeishuCardMiddleware` maps content types to ViewModels →
`CardView<T>.BindMarkdown()` wires `PropertyChanged` → `CardUpdateScheduler` (singleton, 50/sec 1000/min rate limit)
batches updates to Feishu API. Cards use JSON 2.0 with snake_case serialization.
