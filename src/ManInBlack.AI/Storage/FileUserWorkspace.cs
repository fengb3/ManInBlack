using System.Text.Encodings.Web;
using System.Text.Json;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Storage;

/// <summary>
/// 基于文件系统的用户工作空间实现
/// </summary>
[ServiceRegister.Scoped.As<IUserWorkspace>]
public class FileUserWorkspace(AgentContext agentContext) : IUserWorkspace
{
    private readonly string _userId = agentContext.ParentId;
    private string? _sessionId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public string UserId => _userId;

    public string AgentRoot => Path.Combine("agent-workspace");

    /// <summary>
    /// 用户根目录：agent-workspace/{userId}
    /// </summary>
    public string UserRoot => Path.Combine("agent-workspace", _userId);

    /// <summary>
    /// 会话目录：agent-workspace/{userId}/history
    /// </summary>
    public string SessionsDirectory => Path.Combine(UserRoot, "history");

    /// <inheritdoc />
    public string WorkingDirectory => Path.Combine(UserRoot, "workspace");

    /// <summary>
    /// 确保所有必要目录存在
    /// </summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(WorkingDirectory);
    }

    /// <inheritdoc />
    public List<ChatMessage> Initialize()
    {
        EnsureDirectoriesExist();

        // 获取最新的会话文件
        var sessionFile = Directory.GetFiles(SessionsDirectory, "*.session")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (sessionFile != null)
        {
            _sessionId = Path.GetFileNameWithoutExtension(sessionFile);
        }
        else
        {
            _sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var newSessionFile = Path.Combine(SessionsDirectory, $"{_sessionId}.session");
            File.Create(newSessionFile).Dispose();
        }

        // 逐行读取历史消息
        var messages = new List<ChatMessage>();
        foreach (var line in File.ReadLines(Path.Combine(SessionsDirectory, $"{_sessionId}.session")))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var message = JsonSerializer.Deserialize<ChatMessage>(line, JsonOptions);
            if (message != null) messages.Add(message);
        }

        return messages;
    }

    /// <inheritdoc />
    public void AppendHistoryChatMessage(ChatMessage message)
    {
        if (_sessionId == null)
            throw new InvalidOperationException("会话尚未初始化，请先调用 Initialize()。");

        var sessionFile = Path.Combine(SessionsDirectory, $"{_sessionId}.session");
        var json = JsonSerializer.Serialize(message, JsonOptions);
        File.AppendAllText(sessionFile, json + Environment.NewLine);
    }

    /// <summary>
    /// 创建新的会话，后续读写将切换到新会话
    /// </summary>
    public void NewSession()
    {
        EnsureDirectoriesExist();
        _sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var newSessionFile = Path.Combine(SessionsDirectory, $"{_sessionId}.session");
        File.Create(newSessionFile).Dispose();
    }
}