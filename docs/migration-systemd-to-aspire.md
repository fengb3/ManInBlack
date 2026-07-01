# systemd → Aspire 迁移手册

从 systemd 原生二进制部署迁移到 Aspire AppHost + Docker Compose + Podman 容器化部署的实测指南。适用于 .NET 10 项目、Debian 12 目标机、本地 Docker 构建镜像的场景。

## 前提

- 目标机:Linux Debian 12 (bookworm)
- 开发机:.NET 10 SDK + Aspire 13.4.6 AppHost
- 开发机:Docker(用于构建 `linux/amd64` 镜像)
- 目标机:Podman

## 1. AppHost 接入 Docker Compose

在 AppHost 项目引入 `Aspire.Hosting.Docker` NuGet 包,`Program.cs` 添加一行:

```csharp
builder.AddDockerComposeEnvironment("prod");
```

运行 AppHost 后会在 `aspire-output/` 下生成 `compose.yaml` 及 `.env` 文件。

**注意事项:**

- `AddProject` 返回的是 `ProjectResource`,而非 `ContainerResource`。Aspire 13.4.6 中 `WithBindMount` 等 API 要求 `ContainerResource`,类型不匹配会编译失败(CS0311)。
- 因此 **卷挂载、`HOME` 环境变量、端口绑定、capability 添加等容器级配置不要写进 `Program.cs`**,统一在生成的 compose 文件中手动配置。

## 2. Dockerfile

Dockerfile 的 `context` 为仓库根目录。创建 `.dockerignore` 排除无关文件:

```
bin/
obj/
node_modules/
.git/
deploy/
```

### 2.1 带 .NET 前端的项目(如 Dashboard)

前端需要在独立 **node 阶段** 构建:

```dockerfile
# ---- 前端构建 ----
FROM node:22-alpine AS node-build
WORKDIR /src
COPY demo/Dashboard/client/package*.json ./
RUN npm ci
COPY demo/Dashboard/client/ .
RUN npm run build

# ---- .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /src
COPY --from=node-build /src/dist demo/Dashboard/client/dist
COPY . .
RUN dotnet publish demo/Dashboard -c Release -o /app --self-contained false /p:BuildClient=false

# ---- 运行时 ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=sdk /app .
EXPOSE 5080
ENTRYPOINT ["dotnet", "Dashboard.dll"]
```

**关键点:**

- csproj 的前端构建 target 需加 `Condition="'$(BuildClient)' != 'false'"`,在 Docker 构建中用 `/p:BuildClient=false` 跳过 csproj 内的前端构建(已由 node 阶段完成)。
- Vite 的 `outDir` 可能配置为 `../wwwroot`(而非默认 `dist`),COPY 时按实际路径调整。
- .NET 10 **没有** `aspnet:10.0-bookworm-slim` tag,使用 `aspnet:10.0`(Ubuntu Noble)或 alpine 变体。

### 2.2 需要 node/python/bwrap 的项目(如 FeishuAdaptor)

运行时阶段使用 **非 chiseled** 基座(chiseled 镜像无 shell/apt,无法安装额外依赖):

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
    nodejs python3 bubblewrap curl \
    && rm -rf /var/lib/apt/lists/*
```

`--no-install-recommends` 节省镜像体积。

## 3. 本地构建

三步走:

```bash
# 1. Aspire 导出 compose 清单(产物在项目下 aspire-output/)
dotnet run --project demo/AppHost

# 2. 构建各服务镜像(linux/amd64)
docker build -t feishu:latest -f deploy/Dockerfile.feishu .
docker build -t dashboard:latest -f deploy/Dockerfile.dashboard .

# 3. 打包镜像 tar
docker save -o images.tar feishu:latest dashboard:latest
```

**注意:** `aspire publish` 的产物在项目目录下的 `aspire-output/`,**不是** `--output` 指定路径。

`images.tar` 远小于各镜像之和——共享 base 层去重,属于正常现象。

## 4. 目标机 Podman

### 4.1 Podman 版本

Debian 12 (bookworm) 的 Podman 为 **4.3.1**,backports 仓库也只有 4.3.1,没有 5.x。

### 4.2 Compose 工具

Podman 5 才内置 `podman compose`;Podman 4.3 使用 **`podman-compose`**:

```bash
pipx install podman-compose
```

**不要**用 `pip install`——Debian 12 的 pip 受 PEP 668 `externally-managed` 限制,会报错。也**不要**加 `--break-system-packages`。

`pipx install` 会把 `podman-compose` 装到 `~/.local/bin`。

### 4.3 PATH 问题

非交互 shell(如 systemd unit、脚本)不会加载 `~/.profile`/`~/.bashrc`,需要**手动将 `~/.local/bin` 加入 PATH**:

```bash
export PATH="$HOME/.local/bin:$PATH"
```

在 systemd unit 中通过 `Environment=PATH=...` 设置(见第 10 节)。

## 5. 数据与 HOME

### 5.1 HOME 环境变量

容器以 root 运行,`HOME=/root`。**不要**把 `HOME` 设成 `<data-path>`,否则应用解析 `~/.man-in-black` 时会变成 `<data-path>/.man-in-black` 嵌套路径,找不到 `settings.json` 和数据库。

### 5.2 Bind Mount

compose 中:

```yaml
volumes:
  - <data-path>:<data-path>
user: "0:0"
```

容器内应用使用 `<data-path>` 作为数据目录(需在应用配置中指定,而非依赖 `~`)。

### 5.3 SQLite 并发

WAL 模式下,写服务(如 FeishuAdaptor)正常读写,只读服务(如 Dashboard)以 `Mode=ReadOnly` 打开数据库,跨容器并发安全。

## 6. 健康检查

ServiceDefaults 的 `MapDefaultEndpoints` 默认**只在 Development 环境映射** `/health` 和 `/alive`。生产环境需要也映射出来供 Compose healthcheck 使用。

修改 ServiceDefaults 或各项目的健康检查注册,让 Production 环境也映射 `/health` 端点。

compose 中的 healthcheck:

```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:5080/health"]
  interval: 30s
  timeout: 5s
  retries: 3
```

镜像中需包含 `curl`(运行时阶段 `apt install curl`)。

## 7. 切换流程

```bash
# 1. 停止旧的 systemd 服务(避免双写 DB / 飞书 WebSocket 同一 AppId 双连)
sudo systemctl stop feishu.service
sudo systemctl stop dashboard.service

# 2. 加载镜像
podman load -i images.tar

# 3. 启动容器
cd <compose-dir> && podman-compose up -d
```

**务必先停旧服务再启新容器**,否则同一 SQLite DB 会被两个进程同时写入,或飞书 WebSocket 用同一 AppId 建立两个连接导致冲突。

## 8. 首次部署 Dashboard(fail-closed 服务)

Dashboard 是 fail-closed 设计:**`settings.json` 中必须有 `Dashboard:Password`**,否则启动时抛异常。

首次上生产时,`settings.json` 中可能缺少 `Dashboard` 节。需**逐段合并**加入:

```json
{
  "Dashboard": {
    "Password": "<your-password>"
  }
}
```

**不要**整份覆盖 `settings.json`——保留已有配置逐字节不动。操作前先备份:

```bash
cp settings.json settings.json.bak
```

## 9. 访问

端口只绑 `127.0.0.1`,公网不可达。通过 SSH 隧道访问:

```bash
ssh -N -L 5080:localhost:5080 <server-host>
```

本地浏览器访问 `http://localhost:5080`。

## 10. 开机自启

编写 systemd unit 管理 Podman Compose:

```ini
[Unit]
Description=ManInBlack Podman Compose
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=<your-user>
Environment=PATH=/home/<your-user>/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin
WorkingDirectory=<compose-dir>
ExecStart=podman-compose up
ExecStop=podman-compose down
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

启用并启动:

```bash
sudo systemctl enable --now maninblack-compose.service
```

同时禁用(而非删除)旧的 systemd 服务,保留 unit 文件以便回滚:

```bash
sudo systemctl disable feishu.service
sudo systemctl disable dashboard.service
```

## 11. 回滚

数据卷(bind mount)未动,旧服务可原样接管:

```bash
# 1. 停容器
cd <compose-dir> && podman-compose down

# 2. 重启旧 systemd 服务
sudo systemctl start feishu.service
sudo systemctl start dashboard.service
```

## 踩坑总结

| 坑 | 说明 |
|----|------|
| chiseled 镜像无 shell/apt | 需要 node/python/bwrap 的项目用非 chiseled 基座 + `apt install` |
| bwrap 容器内需特权 | `cap_add: SYS_ADMIN` + `security_opt: apparmor=unconfined`;`UseSandbox` 是 opt-in,默认关 |
| .NET 10 无 bookworm-slim tag | 使用 `aspnet:10.0`(Ubuntu Noble)或 alpine |
| Podman 4.3 无内置 compose | 用 `pipx install podman-compose` 安装独立工具 |
| pip 被 PEP 668 拦截 | 用 `pipx install`,不要 `pip install` 或 `--break-system-packages` |
| HOME 不能设成数据路径 | `HOME=/root`,数据路径通过应用配置指定,避免 `~/.man-in-black` 嵌套 |
| dev/prod settings.json 不可整份覆盖 | 逐段合并新增配置,保留已有配置不动 |
| 健康检查仅 Development 映射 | Production 也需映射 `/health`,compose healthcheck 用 `curl` 探测 |
| aspire publish 产物路径 | 在项目目录下 `aspire-output/`,不是 `--output` 指定路径 |
| `AddProject` 返回 `ProjectResource` | `WithBindMount` 等 API 要求 `ContainerResource`,Aspire 13.4.6 编译不通过;容器配置放 compose |
