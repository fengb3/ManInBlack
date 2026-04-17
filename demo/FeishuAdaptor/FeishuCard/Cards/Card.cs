using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeishuAdaptor.FeishuCard.Cards;
// ReSharper disable ClassNeverInstantiated.Global

/// <summary>
/// 飞书消息卡片 JSON 2.0 顶层结构。
/// </summary>
public record Card
{
    /// <summary>
    /// 卡片 JSON 的版本号，固定为 "2.0"。
    /// </summary>
    public string Schema { get; set; } = "2.0";

    /// <summary>
    /// 卡片配置。
    /// </summary>
    public CardConfig? Config { get; set; }

    /// <summary>
    /// 卡片标题区域。
    /// </summary>
    public CardHeader? Header { get; set; }

    /// <summary>
    /// 卡片主体区域。
    /// </summary>
    public CardBody? Body { get; set; }

    /// <summary>
    /// 卡片点击跳转链接。
    /// </summary>
    public CardLink? CardLink { get; set; }

    /// <summary>
    /// 将卡片序列化为 JSON 字符串。
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, CardJsonSerializerOptions.Options);
    }
}

/// <summary>
/// 卡片配置。
/// </summary>
public record CardConfig
{
    /// <summary>
    /// 是否启用流式模式。启用后可通过 API 更新卡片内容。
    /// </summary>
    public bool? StreamingMode { get; set; }

    /// <summary>
    /// 流式模式下的更新频率和步长配置。
    /// </summary>
    public CardStreamingConfig? StreamingConfig { get; set; }

    /// <summary>
    /// 是否允许转发卡片。
    /// </summary>
    public bool? EnableForward { get; set; }

    /// <summary>
    /// 是否允许更新所有已发送的同 ID 卡片。
    /// </summary>
    public bool? UpdateMulti { get; set; }

    /// <summary>
    /// 卡片宽度模式。可取值：
    /// <list type="bullet">
    /// <item><description>default：默认宽度 600px</description></item>
    /// <item><description>compact：紧凑宽度 400px</description></item>
    /// <item><description>fill：撑满屏幕宽度</description></item>
    /// </list>
    /// </summary>
    public string? WidthMode { get; set; }

    /// <summary>
    /// 被转发的卡片是否允许交互。
    /// </summary>
    public bool? EnableForwardInteraction { get; set; }

    /// <summary>
    /// 卡片摘要内容，用于消息列表页展示。
    /// </summary>
    public string? SummaryContent { get; set; }

    /// <summary>
    /// 卡片支持的语言列表。
    /// </summary>
    public List<string>? Locales { get; set; }

    /// <summary>
    /// 是否使用自定义翻译。
    /// </summary>
    public bool? UseCustomTranslation { get; set; }

    /// <summary>
    /// 卡片样式配置。
    /// </summary>
    public CardStyle? Style { get; set; }
}

/// <summary>
/// 流式模式配置 — 控制卡片内容更新的频率和策略。
/// </summary>
public record CardStreamingConfig
{
    /// <summary>
    /// 各端的更新频率（毫秒）。key 为端标识，value 为毫秒数。
    /// </summary>
    public object? PrintFrequencyMs { get; set; }

    /// <summary>
    /// 各端的更新步长。key 为端标识，value 为步长值。
    /// </summary>
    public object? PrintStep { get; set; }

    /// <summary>
    /// 更新策略。可取值：
    /// <list type="bullet">
    /// <item><description>fast：快速更新</description></item>
    /// <item><description>delay：延迟更新</description></item>
    /// </list>
    /// </summary>
    public string? PrintStrategy { get; set; }
}

/// <summary>
/// 卡片样式配置。
/// </summary>
public record CardStyle
{
    /// <summary>
    /// 全局文本大小。
    /// </summary>
    public string? TextSize { get; set; }

    /// <summary>
    /// 全局颜色配置。key 为颜色标识，value 为颜色值。
    /// </summary>
    public Dictionary<string, string>? Color { get; set; }
}

/// <summary>
/// 卡片标题区域。
/// </summary>
public record CardHeader
{
    /// <summary>
    /// 标题文本（tag 为 plain_text 或 lark_md）。
    /// </summary>
    public TextElement? Title { get; set; }

    /// <summary>
    /// 副标题。
    /// </summary>
    public TextElement? Subtitle { get; set; }

    /// <summary>
    /// 标题颜色模板。可取值：blue, wathet, turquoise, green, yellow, orange, red, carmine, violet, purple, indigo, grey, default。
    /// </summary>
    public string? Template { get; set; }

    /// <summary>
    /// 标题图标。
    /// </summary>
    public IconElement? Icon { get; set; }

    /// <summary>
    /// 标题后缀标签列表。
    /// </summary>
    public List<TextTagElement>? TextTagList { get; set; }

    /// <summary>
    /// 标题区域的内边距。
    /// </summary>
    public string? Padding { get; set; }
}

/// <summary>
/// 卡片主体区域。
/// </summary>
public record CardBody
{
    /// <summary>
    /// 组件的排列方向。可取值：
    /// <list type="bullet">
    /// <item><description>vertical：垂直排列</description></item>
    /// <item><description>horizontal：水平排列</description></item>
    /// </list>
    /// </summary>
    public string? Direction { get; set; }

    /// <summary>
    /// 主体区域内边距。
    /// </summary>
    public string? Padding { get; set; }

    /// <summary>
    /// 组件的水平间距。可选值：
    /// <list type="bullet">
    /// <item><description>small：4px</description></item>
    /// <item><description>medium：8px</description></item>
    /// <item><description>large：12px</description></item>
    /// <item><description>extra_large：16px</description></item>
    /// <item><description>具体数值，如 20px。取值范围 [0,99]px</description></item>
    /// </list>
    /// </summary>
    public string? HorizontalSpacing { get; set; }

    /// <summary>
    /// 组件水平对齐方式。可取值：
    /// <list type="bullet">
    /// <item><description>left：左对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>right：右对齐</description></item>
    /// </list>
    /// </summary>
    public string? HorizontalAlign { get; set; }

    /// <summary>
    /// 组件的垂直间距。可选值同 HorizontalSpacing。
    /// </summary>
    public string? VerticalSpacing { get; set; }

    /// <summary>
    /// 组件垂直对齐方式。可取值：
    /// <list type="bullet">
    /// <item><description>top：上对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>bottom：下对齐</description></item>
    /// </list>
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 卡片主体内嵌的组件列表。
    /// </summary>
    public List<CardElement> Elements { get; set; } = [];
}

/// <summary>
/// 卡片点击跳转链接 — 支持按平台配置不同链接。
/// </summary>
public record CardLink
{
    /// <summary>
    /// 兜底的链接地址。
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// PC 端的链接地址。
    /// </summary>
    public string? PcUrl { get; set; }

    /// <summary>
    /// iOS 端的链接地址。
    /// </summary>
    public string? IosUrl { get; set; }

    /// <summary>
    /// Android 端的链接地址。
    /// </summary>
    public string? AndroidUrl { get; set; }
}

/// <summary>
/// 序列化配置 — 使用 snake_case 命名策略，忽略 null 值。
/// </summary>
internal static class CardJsonSerializerOptions
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

/// <summary>
/// 将 PascalCase 属性名转换为 snake_case JSON 属性名。
/// </summary>
internal class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}
