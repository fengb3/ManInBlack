# 飞书 bot 生产镜像。(不加 # syntax= 指令:--mount 缓存挂载由 BuildKit 内置前端支持,且避免经代理拉 docker.io/docker/dockerfile:1)
# 骨架由 Rider「Add Docker Support」生成(它正确解析了 6 工程 ProjectReference 图,
# 自动写出多工程 .csproj COPY 行);此处叠加:BuildKit 缓存挂载 + 运行时依赖 + 生产端口配置。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# 只拷 restore 依赖图的 6 个 .csproj(镜像仓库相对路径),让 restore 独立成缓存层:
# 仅当 .csproj 变化(增删包/版本号/ProjectReference)才失效,改源码不再重下 NuGet 包。
# 注:顶层目录是小写 bwarp(Rider 误生成大写 Bwarp,Linux 构建会 not found)。
COPY ["demo/FeishuAdaptor/FeishuAdaptor.csproj", "demo/FeishuAdaptor/"]
COPY ["src/ManInBlack.AI/ManInBlack.AI.csproj", "src/ManInBlack.AI/"]
COPY ["src/ManInBlack.AI.Abstraction/ManInBlack.AI.Abstraction.csproj", "src/ManInBlack.AI.Abstraction/"]
COPY ["bwarp/Bwarp/Bwarp.csproj", "bwarp/Bwarp/"]
COPY ["src/ManInBlack.AI.SourceGenerator/ManInBlack.AI.SourceGenerator.csproj", "src/ManInBlack.AI.SourceGenerator/"]
COPY ["demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj", "demo/AppHost.ServiceDefaults/"]
# restore 受开发机 TUN 代理并发 TLS 抖动(decryption failed / bad record mac)影响;
# 外层重试 + nuget 缓存挂载(已下载的包不重下)逐次收敛,直到全部包到位。隔离 restore 能成、
# 仅 aspire 并行 build(feishu+dashboard 同时 restore)时并发连接过多耗尽 NuGet 内置重试。
RUN --mount=type=cache,target=/root/.nuget/packages \
    for i in 1 2 3 4 5 6; do \
      if dotnet restore "demo/FeishuAdaptor/FeishuAdaptor.csproj"; then break; fi; \
      [ "$i" = "6" ] && { echo "❌ dotnet restore 6 次均失败"; exit 1; }; \
      echo "⚠️ dotnet restore 第 $i 次失败(代理 TLS 抖动),重试中..."; sleep 3; \
    done
COPY . .
WORKDIR "/src/demo/FeishuAdaptor"
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build "./FeishuAdaptor.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish "./FeishuAdaptor.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish /p:UseAppHost=false

# 非 chiseled:.NET 10 未出 bookworm-slim 变体,且需 apt 装 node/python/bwrap;用默认 aspnet tag(Ubuntu Noble 24.04)。
# 不设 USER:生产 compose 强制 User=0:0,且 agent 的 bwrap 沙箱 + /root/.man-in-black 卷都需要 root。
FROM base AS final
WORKDIR /app
# 换国内 apt 源 + HTTPS + 重试:开发机透明代理(fake-IP 198.18.x)劫持 HTTP 吐陈旧缓存→Hash Sum
# mismatch(HTTPS 走 TLS 绕开);并发 build 时代理偶发 TLS 抖动→"Error reading from server",配 Acquire::Retries。
RUN sed -i 's|http://archive.ubuntu.com|https://mirrors.aliyun.com|g; s|http://security.ubuntu.com|https://mirrors.aliyun.com|g' /etc/apt/sources.list.d/ubuntu.sources \
 && apt-get -o Acquire::Retries=10 update && apt-get -o Acquire::Retries=10 install -y --no-install-recommends \
      nodejs npm python3 bubblewrap ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
# 端口不在此设定:由 Aspire(Program.cs WithEnvironment 注入 ASPNETCORE_URLS)统一控制,镜像端口无关。
ENTRYPOINT ["dotnet", "FeishuAdaptor.dll"]
