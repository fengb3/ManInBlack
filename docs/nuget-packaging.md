# NuGet 打包与发布

本文档说明 ManInBlack 如何打包为 NuGet 包、包之间的依赖关系、以及如何发布与本地消费。

---

## 包结构

| 包 | 项目 | 内容 |
| ---- | ---- | ---- |
| `ManInBlack.AI` | `src/ManInBlack.AI` | 主包：DI 入口（`AddManInBlack`）、中间件管道、内置工具、MCP、SQLite 持久化。**内嵌 `[AiTool]`/`[SlashCommand]` 源生成器**（位于包的 `analyzers/netstandard2.0/` 目录，装包即用） |
| `ManInBlack.AI.Abstraction` | `src/ManInBlack.AI.Abstraction` | 契约层：`AgentDefinition`、`AgentMiddleware`、`IToolExecutor`、Hook、存储等抽象。供只写扩展（中间件/工具/Hook）而不引主包的场景使用 |
| `ManInBlack.Bwarp` | `bwarp/Bwarp` | Linux bubblewrap 沙盒封装。主包的传递依赖，也可独立使用（程序集名仍为 `Bwarp`） |
| `ManInBlack.AI.SourceGenerator` | `src/ManInBlack.AI.SourceGenerator` | 源生成器独立包（development dependency，不流入下游）。已内嵌于主包，一般无需单独安装 |

依赖关系：

```
ManInBlack.AI ──► ManInBlack.AI.Abstraction
              ──► ManInBlack.Bwarp
              ──► (内嵌) ManInBlack.AI.SourceGenerator
```

## 元数据与版本管理

- 仓库根 `Directory.Build.props` 统一维护 `Version`、`Authors`、`RepositoryUrl`、`PackageLicenseExpression`（MIT）、符号包（snupkg）与 SourceLink 设置，并对所有项目默认 `IsPackable=false`。
- 四个库项目各自声明 `PackageId`、`Description`、`PackageTags`，并显式 `IsPackable=true`。
- **发版时只需改 `Directory.Build.props` 里的 `<Version>`**，四个包版本保持一致（主包对 Abstraction/Bwarp 的依赖版本来自各自项目的 Version，`dotnet pack` 自动对齐）。
- 三个 C# 库启用了 `GenerateDocumentationFile`（XML 注释进入包，消费方有 IntelliSense 文档），并以 `NoWarn CS1591` 容忍未注释的公共成员。

## 打包

```bash
# 打包全部（输出到 artifacts/packages，目录已被 .gitignore 忽略）
dotnet pack ManInBlack.slnx -c Release -o artifacts/packages

# 或单独打包主包（会自动带上依赖项目）
dotnet pack src/ManInBlack.AI -c Release -o artifacts/packages
```

产物为 `.nupkg` + `.snupkg`（符号包）。对 demo/test 项目出现的"无法打包此项目"警告是预期行为
（根 `Directory.Build.props` 默认 `IsPackable=false`），可忽略；若想完全避开，可逐项目打包。

### 主包内的源生成器

主包 csproj 中的这两个 `None` 项把生成器及其运行时依赖打进 `analyzers/` 目录：

```xml
<None Include="..\ManInBlack.AI.SourceGenerator\bin\$(Configuration)\netstandard2.0\ManInBlack.AI.SourceGenerator.dll"
      Pack="true" PackagePath="analyzers/netstandard2.0/" />
<None Include="$(PkgFengb3_EasyCodeBuilder)/lib/netstandard2.0/Fengb3.EasyCodeBuilder.dll"
      Pack="true" PackagePath="analyzers/netstandard2.0/" />
```

注意：`Fengb3.EasyCodeBuilder` 在主包里以 `ExcludeAssets=all` 引用，只为取 `$(Pkg...)` 路径，**不会**出现在 nuspec 依赖中。

## 发布

```bash
dotnet nuget push artifacts/packages/ManInBlack.AI.0.1.0.nupkg \
    --api-key <API_KEY> --source https://api.nuget.org/v3/index.json
```

snupkg 用同一条命令随 nupkg 一起推送（NuGet.org 会同时接收）。若使用 GitHub Packages 等私有源，把 `--source` 换成对应 endpoint 并配置 credential provider 或 `nuget.config`。

## 本地消费（不发布）

方式一：本地文件夹源，最接近真实消费体验：

```bash
dotnet nuget add source <repo>/artifacts/packages -n MibLocal
dotnet add package ManInBlack.AI
```

方式二（仓库内 demo 一直采用的方式）：直接项目引用。

```bash
dotnet add reference <repo>/src/ManInBlack.AI/ManInBlack.AI.csproj
```

项目引用模式下源生成器走 `OutputItemType="Analyzer"` 的 ProjectReference，与包内嵌行为等价。

## 常见检查清单

- 改了公共 API 后记得跑 `dotnet pack` 并检查 nupkg：`lib/net10.0/`、`analyzers/netstandard2.0/`、nuspec 依赖列表是否齐全。
- 主包 TargetFramework 为 `net10.0` 单目标；消费方需 .NET 10。
- `Microsoft.EntityFrameworkCore.Design` 在主包为 `PrivateAssets=all`，不会流入消费者。
