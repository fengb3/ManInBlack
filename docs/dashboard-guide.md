# Dashboard 指南

ManInBlack Dashboard 是一个**只读**的 Web 应用，用于浏览器查看 SQLite 中的会话消息、用户，并支持全文搜索。团队部署常驻。

## 配置

`~/.man-in-black/settings.json` 新增节（密码必填，缺省则**拒绝启动**）：

```json
"Dashboard": { "Password": "a-long-random-shared-secret" }
```

Dashboard 直读同一个 `maninblack.db`（只读连接，WAL 下与 FeishuAdaptor 并发安全），不建表、不迁移。

## 开发

```bash
# 后端 API（:5080）
dotnet run --project demo/Dashboard
# 前端 Vite（:5173，proxy /api → :5080）
cd demo/Dashboard/client && npm run dev
```

浏览器访问 `http://localhost:5173`。

> 也可用 Aspire 一条命令同时启动飞书 + Dashboard + 前端:`dotnet run --project demo/AppHost`。详见 [Aspire 编排指南](./aspire-guide.md)。

## 发布与部署

```bash
dotnet publish demo/Dashboard -c Release -o ./publish
```

`dotnet publish` 经 MSBuild target 自动执行 `npm ci && npm run build`，产物落到 `wwwroot/`。**发布机需 Node**，运行时仅 .NET + 静态文件。

部署沿用 FeishuAdaptor 模式：`publish linux-x64` + systemd + 反向代理；应用层密码之外可在反向代理叠一层 basic auth。

## 安全

- cookie 鉴权，密码固定时长比对（防计时侧信道）；fail-closed。
- 工具调用/结果 JSON 走 `JSON.stringify` 插入，React 默认转义，无 XSS 注入面；文本块经 `react-markdown` + `remark-gfm` 插件渲染（支持 GFM 表格、删除线、任务列表、自动链接），**未启用 `rehype-raw`**——LLM 输出的原始 HTML 一律转义不执行，无 XSS 面。
- 严格只读：连接串 `Mode=ReadOnly`，无任何写端点。
