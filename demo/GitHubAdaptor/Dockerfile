FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["demo/GitHubAdaptor/GitHubAdaptor.csproj", "demo/GitHubAdaptor/"]
RUN dotnet restore "demo/GitHubAdaptor/GitHubAdaptor.csproj"
COPY . .
WORKDIR "/src/demo/GitHubAdaptor"
RUN dotnet build "./GitHubAdaptor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GitHubAdaptor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# 安装 gh CLI 和 bubblewrap（沙盒执行）
RUN apt-get update && apt-get install -y gh bubblewrap && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GitHubAdaptor.dll"]