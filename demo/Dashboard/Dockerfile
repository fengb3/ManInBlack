# Dashboard 生产镜像。(不加 # syntax= 指令:--mount 缓存挂载由 BuildKit 内置前端支持,且避免经代理拉 docker.io/docker/dockerfile:1)
# 骨架由 Rider 生成;此处叠加:Node 前端构建阶段(Rider 漏了)+ BuildKit 缓存挂载 + 生产端口配置。

# ── Node 构建前端(package*.json 缓存层)──
FROM node:20-bookworm-slim AS client
WORKDIR /client
COPY demo/Dashboard/client/package*.json ./
# 注:不给 npm ci 加 --mount=type=cache,target=/root/.npm —— 实测会触发 npm/cli#4828
# (optional dep 装不进 node_modules,rollup 的 @rollup/rollup-linux-x64-gnu 丢失 → npm run build
# 报 MODULE_NOT_FOUND)。去掉该 cache mount 后正常;nuget 的 cache mount 不受此 bug 影响,保留。
RUN npm ci
COPY demo/Dashboard/client/ ./
RUN npm run build          # 产物 → /wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# 只拷 restore 依赖图的 6 个 .csproj(镜像仓库相对路径),让 restore 独立成缓存层。
# 注:顶层目录是小写 bwarp(Rider 误生成大写 Bwarp,Linux 构建会 not found)。
COPY ["demo/Dashboard/Dashboard.csproj", "demo/Dashboard/"]
COPY ["src/ManInBlack.AI/ManInBlack.AI.csproj", "src/ManInBlack.AI/"]
COPY ["src/ManInBlack.AI.Abstraction/ManInBlack.AI.Abstraction.csproj", "src/ManInBlack.AI.Abstraction/"]
COPY ["bwarp/Bwarp/Bwarp.csproj", "bwarp/Bwarp/"]
COPY ["src/ManInBlack.AI.SourceGenerator/ManInBlack.AI.SourceGenerator.csproj", "src/ManInBlack.AI.SourceGenerator/"]
COPY ["demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj", "demo/AppHost.ServiceDefaults/"]
# restore 受开发机 TUN 代理并发 TLS 抖动(decryption failed / bad record mac)影响;
# 外层重试 + nuget 缓存挂载(已下载的包不重下)逐次收敛。详见 FeishuAdaptor/Dockerfile 同名注释。
RUN --mount=type=cache,target=/root/.nuget/packages \
    for i in 1 2 3 4 5 6; do \
      if dotnet restore "demo/Dashboard/Dashboard.csproj"; then break; fi; \
      [ "$i" = "6" ] && { echo "❌ dotnet restore 6 次均失败"; exit 1; }; \
      echo "⚠️ dotnet restore 第 $i 次失败(代理 TLS 抖动),重试中..."; sleep 3; \
    done
COPY . .
# 用阶段 1 构建的前端覆盖 wwwroot(.dockerignore 已排除仓库里的 wwwroot,避免陈旧前端)。
COPY --from=client /wwwroot demo/Dashboard/wwwroot/
WORKDIR "/src/demo/Dashboard"
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build "./Dashboard.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# BuildClient=false:关掉 csproj 里 BeforeTargets=Publish 的 npm 构建(前端已由 client 阶段产出)。
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "./Dashboard.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish /p:UseAppHost=false /p:BuildClient=false

FROM base AS final
WORKDIR /app
# 换国内 apt 源 + HTTPS + 重试:开发机透明代理(fake-IP 198.18.x)劫持 HTTP 吐陈旧缓存→Hash Sum
# mismatch(HTTPS 走 TLS 绕开);并发 build 时代理偶发 TLS 抖动→"Error reading from server",配 Acquire::Retries。
RUN sed -i 's|http://archive.ubuntu.com|https://mirrors.aliyun.com|g; s|http://security.ubuntu.com|https://mirrors.aliyun.com|g' /etc/apt/sources.list.d/ubuntu.sources \
 && apt-get -o Acquire::Retries=10 update && apt-get -o Acquire::Retries=10 install -y --no-install-recommends curl ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
# 端口不在此设定:由 Aspire(Program.cs WithEnvironment 注入 ASPNETCORE_URLS=5080)统一控制。
ENTRYPOINT ["dotnet", "Dashboard.dll"]
