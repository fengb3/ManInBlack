using System.Text.Json.Serialization;
// ReSharper disable ClassNeverInstantiated.Global

namespace FeishuAdaptor.FeishuCard.Cards;

/// <summary>
/// 卡片组件基类 — 所有 body.elements 中的组件都继承此类型。
/// 通过 <see cref="JsonPolymorphicAttribute"/> 实现多态序列化，自动写入 "tag" 字段。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tag")]
[JsonDerivedType(typeof(ColumnSetElement), "column_set")]
[JsonDerivedType(typeof(FormElement), "form")]
[JsonDerivedType(typeof(InteractiveContainerElement), "interactive_container")]
[JsonDerivedType(typeof(CollapsiblePanelElement), "collapsible_panel")]
[JsonDerivedType(typeof(DivElement), "div")]
[JsonDerivedType(typeof(MarkdownElement), "markdown")]
[JsonDerivedType(typeof(ImgElement), "img")]
[JsonDerivedType(typeof(ImgCombinationElement), "img_combination")]
[JsonDerivedType(typeof(HrElement), "hr")]
[JsonDerivedType(typeof(TableElement), "table")]
[JsonDerivedType(typeof(ChartElement), "chart")]
[JsonDerivedType(typeof(PersonElement), "person")]
[JsonDerivedType(typeof(PersonListElement), "person_list")]
[JsonDerivedType(typeof(ButtonElement), "button")]
[JsonDerivedType(typeof(InputElement), "input")]
[JsonDerivedType(typeof(SelectStaticElement), "select_static")]
[JsonDerivedType(typeof(MultiSelectStaticElement), "multi_select_static")]
[JsonDerivedType(typeof(SelectPersonElement), "select_person")]
[JsonDerivedType(typeof(MultiSelectPersonElement), "multi_select_person")]
[JsonDerivedType(typeof(DatePickerElement), "date_picker")]
[JsonDerivedType(typeof(PickerTimeElement), "picker_time")]
[JsonDerivedType(typeof(PickerDatetimeElement), "picker_datetime")]
[JsonDerivedType(typeof(SelectImgElement), "select_img")]
[JsonDerivedType(typeof(OverflowElement), "overflow")]
[JsonDerivedType(typeof(CheckerElement), "checker")]
public abstract record CardElement
{
    /// <summary>
    /// 组件的外边距。值的取值范围为 [-99,99]px。可选值：
    /// <list type="bullet">
    /// <item><description>单值，如 "10px"，表示组件的四个外边距都为 10 px。</description></item>
    /// <item><description>双值，如 "4px 0"，表示组件的上下外边距为 4 px，左右外边距为 0 px。使用空格间隔（边距为 0 时可不加单位）。</description></item>
    /// <item><description>多值，如 "4px 0 4px 0"，表示组件的上、右、下、左的外边距分别为 4px，12px，4px，12px。使用空格间隔。</description></item>
    /// </list>
    /// </summary>
    public string? Margin { get; set; }

    /// <summary>
    /// 组件的唯一标识。JSON 2.0 新增属性。用于在调用组件相关接口中指定组件。
    /// 在同一张卡片内，该字段的值全局唯一。仅允许使用字母、数字和下划线，必须以字母开头，不得超过 20 字符。
    /// </summary>
    public string? ElementId { get; set; }
}

// ───────────────────────── 共享类型 ─────────────────────────

/// <summary>
/// 文本元素 — 用于标题、按钮文本、placeholder 等。
/// Tag 默认 <c>plain_text</c>，需要 Markdown 时设为 <c>lark_md</c>。
/// </summary>
public record TextElement
{
    /// <summary>
    /// 文本标签。可选值：
    /// <list type="bullet">
    /// <item><description><c>plain_text</c>：普通文本内容</description></item>
    /// <item><description><c>lark_md</c>：支持部分 Markdown 语法的文本内容</description></item>
    /// </list>
    /// </summary>
    public string Tag { get; set; } = "plain_text";

    /// <summary>
    /// 文本内容。当 tag 为 lark_md 时，支持部分 Markdown 语法的文本内容。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 文本大小。可取值：
    /// <list type="bullet">
    /// <item><description>heading-0：特大标题（30px）</description></item>
    /// <item><description>heading-1：一级标题（24px）</description></item>
    /// <item><description>heading-2：二级标题（20px）</description></item>
    /// <item><description>heading-3：三级标题（18px）</description></item>
    /// <item><description>heading-4：四级标题（16px）</description></item>
    /// <item><description>heading：标题（16px）</description></item>
    /// <item><description>normal：正文（14px）</description></item>
    /// <item><description>notation：辅助信息（12px）</description></item>
    /// <item><description>xxxx-large ~ x-small：对应具体像素大小</description></item>
    /// </list>
    /// </summary>
    public string? TextSize { get; set; }

    /// <summary>
    /// 文本的颜色。仅在 tag 为 plain_text 时生效。可取值：
    /// <list type="bullet">
    /// <item><description>default：客户端浅色主题下为黑色；深色主题下为白色</description></item>
    /// <item><description>颜色的枚举值，参考颜色枚举值文档</description></item>
    /// </list>
    /// </summary>
    public string? TextColor { get; set; }

    /// <summary>
    /// 文本对齐方式。可取值：
    /// <list type="bullet">
    /// <item><description>left：左对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>right：右对齐</description></item>
    /// </list>
    /// </summary>
    public string? TextAlign { get; set; }

    /// <summary>
    /// 内容最大显示行数，超出设置行的内容用 ... 省略。
    /// </summary>
    public int? Lines { get; set; }

    public TextElement() { }

    public TextElement(string content)
    {
        Content = content;
    }

    public TextElement(string content, string tag)
    {
        Content = content;
        Tag = tag;
    }
}

/// <summary>
/// 图标元素基类 — 支持标准图标和自定义图标。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tag")]
[JsonDerivedType(typeof(StandardIconElement), "standard_icon")]
[JsonDerivedType(typeof(CustomIconElement), "custom_icon")]
public abstract record IconElement;

/// <summary>
/// 标准图标 — 使用飞书图标库中的图标。
/// </summary>
public record StandardIconElement : IconElement
{
    /// <summary>
    /// 图标库中图标的 token，如 "chat_outlined"、"edit_outlined"。
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// 图标的颜色。支持设置线性和面性图标（即 token 末尾为 outlined 或 filled 的图标）的颜色。
    /// </summary>
    public string? Color { get; set; }
}

/// <summary>
/// 自定义图标 — 通过上传图片接口获取 img_key 后使用。
/// </summary>
public record CustomIconElement : IconElement
{
    /// <summary>
    /// 通过上传图片接口获取的 img_key。
    /// <para>参考：https://open.feishu.cn/document/server-docs/im-v1/image/create</para>
    /// </summary>
    public string? ImgKey { get; set; }
}

/// <summary>
/// 交互行为基类 — 定义组件被操作后的响应方式。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CallbackBehavior), "callback")]
[JsonDerivedType(typeof(OpenUrlBehavior), "open_url")]
public abstract record ActionBehavior;

/// <summary>
/// 回调行为 — 用户操作后向服务端回传数据。
/// </summary>
public record CallbackBehavior : ActionBehavior
{
    /// <summary>
    /// 回传数据，可为任意 JSON 对象。
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// 打开链接行为 — 用户操作后跳转到指定链接。
/// </summary>
public record OpenUrlBehavior : ActionBehavior
{
    /// <summary>
    /// 兜底的链接地址。
    /// </summary>
    public string? DefaultUrl { get; set; }

    /// <summary>
    /// PC 端的链接地址。可配置为 lark://msgcard/unsupported_action 声明当前端不允许跳转。
    /// </summary>
    public string? PcUrl { get; set; }

    /// <summary>
    /// iOS 端的链接地址。可配置为 lark://msgcard/unsupported_action 声明当前端不允许跳转。
    /// </summary>
    public string? IosUrl { get; set; }

    /// <summary>
    /// Android 端的链接地址。可配置为 lark://msgcard/unsupported_action 声明当前端不允许跳转。
    /// </summary>
    public string? AndroidUrl { get; set; }
}

/// <summary>
/// 二次确认弹窗 — 用户提交时弹出确认提示，点击确认后才提交。
/// <para>该字段默认提供了确认和取消按钮，只需配置弹窗的标题与内容即可。</para>
/// </summary>
public record ConfirmElement
{
    /// <summary>
    /// 二次确认弹窗标题。
    /// </summary>
    public TextElement? Title { get; set; }

    /// <summary>
    /// 二次确认弹窗的文本内容。
    /// </summary>
    public TextElement? Text { get; set; }
}

/// <summary>
/// 标题后缀标签 — 在卡片标题区域展示标签信息。
/// </summary>
public record TextTagElement
{
    /// <summary>
    /// 标签类型，固定为 "text_tag"。
    /// </summary>
    public string Tag { get; set; } = "text_tag";

    /// <summary>
    /// 标签文本。
    /// </summary>
    public TextElement? Text { get; set; }

    /// <summary>
    /// 标签颜色。可选值：neutral, blue, turquoise, lime, orange, violet, indigo, wathet, green, yellow, red, purple, carmine。
    /// </summary>
    public string? Color { get; set; }
}

/// <summary>
/// 多平台链接配置 — 为不同平台配置对应的链接地址。
/// </summary>
public record MultiUrl
{
    /// <summary>
    /// 兜底的链接地址。
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// PC 端的链接地址。可配置为 lark://msgcard/unsupported_action 声明当前端不允许跳转。
    /// </summary>
    public string? PcUrl { get; set; }

    /// <summary>
    /// iOS 端的链接地址。可配置为 lark://msgcard/unsupported_action 声明当前端不允许跳转。
    /// </summary>
    public string? IosUrl { get; set; }

    /// <summary>
    /// Android 端的链接地址。可配置为 lark://msgcard/unsupported_action 声明当前端不允许跳转。
    /// </summary>
    public string? AndroidUrl { get; set; }
}
