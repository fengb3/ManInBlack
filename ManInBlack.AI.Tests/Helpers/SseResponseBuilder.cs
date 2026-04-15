using System.Text;

namespace ManInBlack.AI.Tests.Helpers;

/// <summary>
/// 构建 SSE (Server-Sent Events) 格式的响应流，用于测试流式 ChatClient
/// </summary>
public static class SseResponseBuilder
{
    /// <summary>
    /// 构建 SSE 流，每个 payload 包装为 "data: {json}\n\n" 格式
    /// </summary>
    public static Stream Build(params string[] jsonDataChunks)
    {
        var ms = new MemoryStream();
        var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        foreach (var chunk in jsonDataChunks)
        {
            writer.Write($"data: {chunk}\n\n");
        }
        writer.Flush();
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// 构建 SSE 流并追加 "data: [DONE]\n\n" 终止符（OpenAI/Gemini 风格）
    /// </summary>
    public static Stream BuildWithDone(params string[] jsonDataChunks)
    {
        var all = jsonDataChunks.Append("[DONE]").ToArray();
        return Build(all);
    }
}
