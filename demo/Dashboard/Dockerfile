# 阶段 1:Node 构建前端
FROM node:20-bookworm-slim AS client
WORKDIR /client
COPY demo/Dashboard/client/package*.json ./
RUN npm ci
COPY demo/Dashboard/client/ ./
RUN npm run build          # 产物 → /wwwroot

# 阶段 2:.NET SDK 发布(关掉 BuildClient,用阶段 1 的 wwwroot)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=client /wwwroot demo/Dashboard/wwwroot/
RUN dotnet publish demo/Dashboard/Dashboard.csproj -c Release -o /app/publish /p:UseAppHost=false /p:BuildClient=false

# 阶段 3:运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "Dashboard.dll"]
