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

本项目的完整写法见 `demo/AppHost/Program.cs`:`AddDockerComposeEnvironment("prod").WithDashboard(false).ConfigureComposeFile(...)` + 各项目 `.PublishAsDockerFile(c => c.WithDockerfile(...))`。运行 `aspire publish`(或 `aspire do prepare-prod` / `aspire deploy`)后会在 AppHost 项目下 `aspire-output/` 生成 `docker-compose.yaml` 及 `.env` 文件(详见第 3 节)。

**注意(13.4.6 实测):**

- `AddProject` 返回 `ProjectResource`,`WithBindMount` 等 API 要 `ContainerResource`,直接调会 CS0311。
- **但** 容器级配置(卷 / `HOME` / 端口 / `cap_add` / `security_opt` / healthcheck)可全部在 `ConfigureComposeFile` 回调里写进代码——`Service` 模型是完整 compose schema,这些字段都有。本项目就采用此方式(见 `ConfigureProdCompose`),生成 compose 直接生产可用,不再手改。
- 各项目必须 `.PublishAsDockerFile(c => c.WithDockerfile(...))` 指定自带 Dockerfile,否则 `aspire do prepare-prod` 会走 .NET SDK 默认容器发布(裸 `aspnet:10.0` 基座,没 node/python/bwrap,且 build 失败)。

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

> **开 sandbox 必须 privileged(重要)**:agent 若开 bwarp 沙箱(`UseSandbox` opt-in;`SandboxPresets.Unshare(User|Pid)` → bwrap `--unshare-user --unshare-pid --proc`),需在 user+pid namespace 挂 /proc,**kernel 4.19 下 `cap_add: SYS_ADMIN` + `security_opt: seccomp=unconfined` 都不够**,必须置 **`privileged: true`**(代价:容器获宿主全部能力,bot 若被攻破≈宿主沦陷;Kylin 无 AppArmor,故 `apparmor=unconfined` 无意义)。本项目在 `ConfigureProdCompose` 里以 `feishu.Privileged = true`(`Service.Privileged`)代码配置,生成的 compose 直接带上;**不开 sandbox 则 `SYS_ADMIN`+`apparmor` 即够,无需 privileged**。详见文末踩坑表。

## 3. 本地构建(开发机)

Aspire 把"生成 compose 清单"和"构建镜像"拆成两个命令,**不要混用**:

- `aspire publish` —— **只**生成 `docker-compose.yaml` + `.env`(占位符未填),**不构建镜像**。`aspire-output/.env` 里会是 `FEISHU_IMAGE=` 这样的空值,仅供查看拓扑。
- `aspire do prepare-prod` —— 生成 compose + 填好值的 `.env.production` + **构建每个服务的镜像**(Aspire 找到/生成各项目的 Dockerfile 后,调底层容器运行时 build)。这才是 aspire 原生的"构建镜像"步骤。
- `aspire deploy` —— 在 `prepare` 之上再跑 `docker compose up -d`(本机一键起容器,适合本机自部署)。

我们的目标是把镜像搬到**远程 rootful Podman 主机(无 registry)**,所以用 `prepare-prod`(只构建、不在本机起容器),再 `save`/`load`。本项目把它封成一条命令(`deploy/build-prod.sh`,内部三步):

```bash
deploy/build-prod.sh
#   1. aspire do prepare-prod  → 生成 docker-compose.yaml + .env.Production + build feishu/dashboard 镜像
#      (用 Program.cs 里 WithDockerfile 指定的 Dockerfile;产物在 deploy/aspire-aliyun/dist/)
#   2. 把 .env.Production 并成单一 .env(服务器侧 podman compose 自动读)
#   3. docker save -o images.tar <两个镜像>
```

**关键区分:** `aspire publish` 只生成 compose、**不 build**;`aspire do prepare-prod`(env 名 `prod`)才 **build 镜像**。镜像 tag 是 Aspire 默认的 `<资源>:<sha>`(如 `feishu:b0064988...`)。**坑(实测多种 API 均如此):无 registry 时,没有任何 Aspire API 能改 build tag**——`WithRemoteImageTag` 单独用、或配 `WithRemoteImageName`,都只改 `.env` 引用、不改 build tag(配 `AddContainerRegistry` 后 `WithRemoteImageName/Tag` 才会真正改 build tag)。`prepare-prod` 的 `.env` 又引用时间戳 tag(同样对不上,那个 tag 只有 `aspire deploy` 才上)。结论:`build-prod.sh` 读实际 build 出的 sha 重写 `.env` 对齐,再 `docker save`。想要固定/版本号 tag,只能 retag 或上 registry。

`images.tar` 远小于各镜像之和——共享 base 层去重,属于正常现象。

下面是 `build-prod.sh` 三步的底层展开(也可手动逐步执行):

### 3.1 选定容器运行时(手动执行时)

Aspire 并行探测 Docker 与 Podman,**正在运行的那个优先,Docker 作为平局赢家**。开发机一般是 Docker Desktop(运行中)→ Aspire 会用 `docker build`。可用环境变量强制:

```bash
# 强制用 Podman 构建(若开发机也装了 Podman)
export ASPIRE_CONTAINER_RUNTIME=podman
```

### 3.2 底层展开:`aspire do prepare-prod`(生成 compose + 镜像)

```bash
# 在仓库根目录(demo/AppHost 是 AppHost 项目)
aspire do prepare-prod --apphost demo/AppHost/ManInBlack.AppHost.csproj
```

产物全部落在 `demo/AppHost/aspire-output/`(或 `--output-path` 指定目录):

| 产物 | 说明 |
|------|------|
| `docker-compose.yaml` | 各服务的编排清单 |
| `.env.production` | 填好值的参数文件(`FEISHU_IMAGE` / `DASHBOARD_IMAGE` 等) |
| 各资源 Dockerfile | Aspire 为 `AddProject` 资源生成/复用的 Dockerfile |

`build-feishu` / `build-dashboard` 流水线步骤会调底层运行时 build 出镜像,镜像名/Tag 与 compose 里 `image:` 字段由**同一套逻辑生成**,因此天然一致。

### 3.3 打包传输到远程 Podman 主机

Aspire 没有原生"导出 tar 给离线远程主机"的步骤,镜像仍需手动 `save` → `load`(共享 base 层去重,tar 远小于各镜像之和):

```bash
# 用 prepare 阶段实际构建出的镜像名(查 .env.production 里的 FEISHU_IMAGE / DASHBOARD_IMAGE)
docker save -o images.tar <feishu-image> <dashboard-image>

# 传到目标机后
sudo podman load -i images.tar
```

> **ℹ️ 关于镜像 tag 对齐:** 手写 `docker build -t` 时曾出现 `docker.io/library/<name>` 与 compose 引用的 `localhost/<name>` 不一致、导致新镜像被静默忽略的坑。改用 `aspire do prepare-prod` 后,compose 的 `image:` 字段与实际构建出的镜像 Tag 由同一套逻辑产出,二者天然一致,**此坑不再适用**。仍要 `save`/`load` 时,从 `.env.production` 复制真实的 `*_IMAGE` 值,不要自己猜 tag。

> **⚠️ 容器运行时无代理 → 镜像内 `dotnet restore` 失败(国内开发机):** `aspire do prepare-prod` 的 `build-*` 步骤**仍然走底层运行时(Docker/Podman)的 build**,即在容器/守护进程内执行 `dotnet restore`,并不会绕过守护进程网络。Docker Desktop 守护进程(WSL2 VM)不走宿主机代理,直连 nuget.org 仍会 SSL EOF(`NU1301: unexpected EOF`)。对策:① 给守护进程配 HTTP_PROXY(指向宿主机 `host.docker.internal:<port>`);② 或先在宿主机 `dotnet publish -o <dir> --os linux`,再用一个只 `COPY <dir>` 的临时 Dockerfile 构建镜像;③ 或 `ASPIRE_CONTAINER_RUNTIME=podman` 改用宿主机 Podman(其 build 走宿主机网络/代理)。(已删除仓库根 `NuGet.Config` —— 它对守护进程内的 restore 无效,因为守护进程连不上镜像站。)

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
  - <data-path>:/root/.man-in-black
user: "0:0"
```

> **注意:** FeishuAdaptor 以 `~/.man-in-black` 为根(`HOME=/root` → `/root/.man-in-black`),其卷须挂到该路径;Dashboard 经 `Storage:RootPath` 显式配置路径,不依赖 HOME。

容器内应用使用挂载目标路径作为数据目录。

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

## 12. 开发机构建环境(TUN 代理;2026-07-03 实测)

开发机跑透明代理(fake-IP 198.18.x,Clash/Surge 类 TUN 模式),`aspire do prepare-prod` 在容器内 build 时踩了三个网络相关的坑,**每次 build 都会复现**,均已修(见 `demo/FeishuAdaptor/Dockerfile`、`demo/Dashboard/Dockerfile`):

1. **`npm ci` 加 `--mount=type=cache,target=/root/.npm` → rollup `Cannot find module @rollup/rollup-linux-x64-gnu`**。触发 [npm/cli#4828](https://github.com/npm/cli/issues/4828)(optional dep 装不进 `node_modules`),且损坏层被 buildx **反复缓存复用**(`#7 CACHED`)。**修:去掉该 cache mount**(`RUN npm ci`)。nuget 的 cache mount 不受此 bug 影响,保留。

2. **apt `Hash Sum mismatch` + `Error reading from server`**。透明代理劫持 HTTP、吐**陈旧** Ubuntu 仓库缓存(Release 新、Packages 旧)→ Hash mismatch;**改 HTTPS 源**绕开(代理无证书无法 MITM 陈旧缓存)。但并发 build 时代理仍偶发 TLS 抖动 → `Error reading from server`,**配 `apt-get -o Acquire::Retries=10`** 重试兜底(update + install 都加)。

3. **`dotnet restore` 并发 → `decryption failed or bad record mac`(SSL_ERROR_SSL)**。feishu+dashboard 两个镜像并行 restore,经 TUN 代理的并发 TLS 连接过载、丢包坏 record。隔离单 build 能成(NuGet 内置重试恢复);并行时耗尽重试。**修:restore 外层 `for i in 1..6` 重试循环**,配合 `--mount=type=cache,target=/root/.nuget/packages`(两个镜像**共享同一缓存**,已下载的包不重下,逐次收敛)。

> 排查手法:`docker run`(不注入 daemon 代理、无 cache mount、单包)能成 → 把变量逐个加回去定位。三坑共同根因是开发机代理在容器内**并发**网络下的不稳定;生产服务器(阿里云)网络干净、运行期不受影响(依赖已烤进镜像层)。构建期三个修复都是"重试/绕开",不改变运行期行为。

## 踩坑总结

| 坑 | 说明 |
|----|------|
| chiseled 镜像无 shell/apt | 需要 node/python/bwrap 的项目用非 chiseled 基座 + `apt install` |
| bwrap 容器内挂 /proc 失败 | agent 开 bwarp 沙箱(`UseSandbox`,opt-in)时,`.Unshare(User|Pid)`(bwrap `--unshare-user --unshare-pid --proc`)在 user+pid ns 挂 proc 报 `Can't mount proc on /newroot/proc: Operation not permitted`;**kernel 4.19 实测仅 `privileged: true` 可用**(`cap_add: SYS_ADMIN`、`security_opt: seccomp=unconfined` 均失败)→ 开 sandbox 时须在 `ConfigureProdCompose` 里 `feishu.Privileged = true`(`Service.Privileged`);不开 sandbox 则 `SYS_ADMIN`+`apparmor` 足够 |
| .NET 10 无 bookworm-slim tag | 使用 `aspnet:10.0`(Ubuntu Noble)或 alpine |
| Podman 4.3 无内置 compose | 用 `pipx install podman-compose` 安装独立工具 |
| pip 被 PEP 668 拦截 | 用 `pipx install`,不要 `pip install` 或 `--break-system-packages` |
| HOME 不能设成数据路径 | `HOME=/root`,数据路径通过应用配置指定,避免 `~/.man-in-black` 嵌套 |
| dev/prod settings.json 不可整份覆盖 | 逐段合并新增配置,保留已有配置不动 |
| 健康检查仅 Development 映射 | Production 也需映射 `/health`,compose healthcheck 用 `curl` 探测 |
| `aspire publish` vs `aspire do prepare-prod` | `publish` 只生成 compose + 空 `.env`(不构建镜像);`prepare-prod` 才构建镜像 + 填 `.env.production`。远程主机部署用 `prepare-prod` |
| Aspire 产物路径 | 默认 AppHost 项目下 `aspire-output/`;可用 `-o/--output-path` 改 |
| `AddProject` 返回 `ProjectResource` | `WithBindMount` 等仍要 `ContainerResource`(CS0311),但容器配置改用 `ConfigureComposeFile` 在代码里写,不再手改 compose |
| 裸 `AddProject` 发布 build 失败 | 默认走 .NET SDK 容器发布(裸基座),必须 `.PublishAsDockerFile(WithDockerfile)` 指定自带 Dockerfile |
| 手写 `docker build -t` 的 tag 与 compose `image:` 不一致 | 曾导致 `podman load` 后新镜像被**静默忽略**(docker.io/library vs localhost)。改用 `aspire do prepare-prod` 后 compose 与镜像 Tag 同源,此坑消除;`save`/`load` 时按 `.env.production` 的 `*_IMAGE` 取真实 tag |
| 飞书长连接 ACK 偶发超时 → 重复回复 | 容器内 ACK 延迟呈**双峰**(<1s 或 >3.7s,中间空白),已排除网络(host-net 仍超时)与 Docker 差异(同代码 Docker 机正常),**根因未明**。**已修复:FeishuAdaptor 改用 webhook 模式(HTTP 200 ACK)后超时消失**;同时保留**兜底——在事件处理器按 `eventId` 去重**,丢弃飞书的重推(同 eventId 在 ~5min 内到达),保证每条消息只处理/回复一次(见 `ImMessageReceiveEventHandler`) |
| 容器运行时无代理 → `aspire do prepare-prod` 的 build 步骤 restore 失败 | `build-*` 步骤仍走底层运行时(Docker/Podman)的 build,守护进程(WSL2 VM)不走宿主机代理 → nuget.org SSL EOF。对策:给守护进程配代理 / 宿主机 `dotnet publish` 后 COPY / 改用 `ASPIRE_CONTAINER_RUNTIME=podman` 走宿主机网络 |
| 开发机 TUN 代理 → 容器内 build 的 npm/apt/nuget 网络抖动 | 透明代理(fake-IP 198.18.x)并发下不稳定:`npm ci` 的 cache mount 触发 npm/cli#4828(rollup MODULE_NOT_FOUND)→ 删 mount;apt HTTP 被吐陈旧缓存 → Hash mismatch(改 HTTPS 源)+ 并发 TLS 抖 → `Acquire::Retries=10`;并行 `dotnet restore` → SSL_ERROR_SSL → 外层重试(nuget cache mount 跨镜像共享收敛)。**每次 build 复现,已修在两个 Dockerfile,详见 §12** |
