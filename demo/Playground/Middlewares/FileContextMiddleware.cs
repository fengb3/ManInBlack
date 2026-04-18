using System.Text;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;

namespace Playground.Middlewares;

/// <summary>
/// 文件上下文中间件示例，将请求/响应记录到文件系统
/// </summary>
public class FileContextMiddleware(string directoryPath) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directoryPath);
        var id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

        // 记录请求
        var requestPath = Path.Combine(directoryPath, $"{id}_request.json");
        await File.WriteAllTextAsync(requestPath,
            $"Messages: {context.Messages.Count}", ct);

        // 收集响应并记录
        var sb = new StringBuilder();
        await foreach (var update in next().WithCancellation(ct))
        {
            sb.Append(update.Text);
            yield return update;
        }

        var responsePath = Path.Combine(directoryPath, $"{id}_response.json");
        await File.WriteAllTextAsync(responsePath, sb.ToString(), ct);
    }
}
