# 阶段 1:.NET SDK 发布
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish demo/FeishuAdaptor/FeishuAdaptor.csproj -c Release -o /app/publish /p:UseAppHost=false

# 阶段 2:运行时(非 chiseled,需 apt 装 node/python/bwrap/curl)
# 注:.NET 10 未发布 bookworm-slim 变体,使用默认 tag(Ubuntu Noble 24.04)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends \
      nodejs npm python3 bubblewrap ca-certificates curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FeishuAdaptor.dll"]
