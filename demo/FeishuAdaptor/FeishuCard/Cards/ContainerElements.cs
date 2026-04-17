namespace FeishuAdaptor.FeishuCard.Cards;
// ReSharper disable ClassNeverInstantiated.Global

// ──────────────── 分栏 (column_set) ────────────────

/// <summary>
/// 分栏容器 — 横向排布多列内容，最多五层嵌套。
/// 不能包含 form 或 table。
/// </summary>
public record ColumnSetElement : CardElement
{
    /// <summary>
    /// 移动端和 PC 端的窄屏幕下，各列的自适应方式。可取值：
    /// <list type="bullet">
    /// <item><description>none：不做布局上的自适应，在窄屏幕下按比例压缩列宽度</description></item>
    /// <item><description>stretch：列布局变为行布局，且每列（行）宽度强制拉伸为 100%</description></item>
    /// <item><description>flow：列流式排布（自动换行），当一行展示不下一列时，自动换至下一行</description></item>
    /// <item><description>bisect：两列等分布局</description></item>
    /// <item><description>trisect：三列等分布局</description></item>
    /// </list>
    /// </summary>
    public string? FlexMode { get; set; }

    /// <summary>
    /// 分栏内组件的水平间距。可选值：
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
    /// 分栏内组件在水平方向上的对齐方式。可取值：
    /// <list type="bullet">
    /// <item><description>left：左对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>right：右对齐</description></item>
    /// </list>
    /// </summary>
    public string? HorizontalAlign { get; set; }

    /// <summary>
    /// 分栏内组件在垂直方向上的对齐方式。可取值：
    /// <list type="bullet">
    /// <item><description>top：上对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>bottom：下对齐</description></item>
    /// </list>
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 分栏的背景色样式。可取值：
    /// <list type="bullet">
    /// <item><description>default：默认的白底样式，客户端深色主题下为黑底样式</description></item>
    /// <item><description>卡片支持的颜色枚举值和 RGBA 语法自定义颜色</description></item>
    /// </list>
    /// <para><strong>注意</strong>：当存在分栏的嵌套时，上层分栏的颜色覆盖下层分栏的颜色。</para>
    /// </summary>
    public string? BackgroundStyle { get; set; }

    /// <summary>
    /// 分栏中列的配置。
    /// </summary>
    public List<Column> Columns { get; set; } = [];

    /// <summary>
    /// 设置点击分栏时的交互配置。当前仅支持跳转交互。
    /// 如果布局容器内有交互组件，则优先响应交互组件定义的交互。
    /// </summary>
    public MultiUrl? Action { get; set; }
}

/// <summary>
/// 分栏中的单列 — 不是 CardElement，仅在 ColumnSet 内使用。
/// </summary>
public record Column
{
    /// <summary>
    /// 列的标签，固定为 "column"。
    /// </summary>
    public string Tag { get; set; } = "column";

    /// <summary>
    /// 列宽度。仅 flex_mode 为 none 时生效。可取值：
    /// <list type="bullet">
    /// <item><description>auto：列宽度与列内元素宽度一致</description></item>
    /// <item><description>weighted：列宽度按 weight 参数定义的权重分布</description></item>
    /// <item><description>具体数值，如 100px。取值范围 [16,600]px</description></item>
    /// </list>
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 当 width 字段取值为 weighted 时生效，表示当前列的宽度占比。取值范围 1 ~ 5 的整数。
    /// </summary>
    public int? Weight { get; set; }

    /// <summary>
    /// 列内组件在垂直方向上的对齐方式。可取值：
    /// <list type="bullet">
    /// <item><description>top：上对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>bottom：下对齐</description></item>
    /// </list>
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 列的背景色样式。可取值：
    /// <list type="bullet">
    /// <item><description>default：默认的白底样式</description></item>
    /// <item><description>卡片支持的颜色枚举值和 RGBA 语法自定义颜色</description></item>
    /// </list>
    /// </summary>
    public string? BackgroundStyle { get; set; }

    /// <summary>
    /// 列的内边距。取值范围 [0,99]px。
    /// </summary>
    public string? Padding { get; set; }

    /// <summary>
    /// 列内组件的排列方向。可取值：
    /// <list type="bullet">
    /// <item><description>vertical：垂直排列</description></item>
    /// <item><description>horizontal：水平排列</description></item>
    /// </list>
    /// </summary>
    public string? Direction { get; set; }

    /// <summary>
    /// 列中内嵌的组件列表。
    /// </summary>
    public List<CardElement> Elements { get; set; } = [];

    /// <summary>
    /// 设置点击列时的交互配置。当前仅支持跳转交互。
    /// </summary>
    public MultiUrl? Action { get; set; }
}

// ──────────────── 表单容器 (form) ────────────────

/// <summary>
/// 表单容器 — 支持用户在前端录入一批表单项后，通过点击一次提交按钮，将表单内容一次回调至服务端。
/// <para>只能出现在卡片根级别，不能嵌套 table 或另一个 form。</para>
/// <para><strong>注意</strong>：表单容器内必须包含一个用于提交表单的按钮组件。</para>
/// </summary>
public record FormElement : CardElement
{
    /// <summary>
    /// 表单容器的唯一标识。用于识别用户提交的数据属于哪个表单容器。
    /// 在同一张卡片内，该字段的值全局唯一。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 表单容器的子节点。可内嵌其它容器类组件和展示、交互组件，不支持内嵌表格、图表和表单容器。
    /// </summary>
    public List<CardElement> Elements { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。用户点击提交时弹出确认提示。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 交互容器 (interactive_container) ────────────────

/// <summary>
/// 交互容器 — 在容器内嵌组件并统一定义交互能力。
/// <para>交互容器允许基于业务需求灵活组合多个交互容器，统一定义样式和交互能力，实现丰富的卡片交互。</para>
/// <para>不能包含 form 或 table。</para>
/// </summary>
public record InteractiveContainerElement : CardElement
{
    /// <summary>
    /// 交互容器的宽度。可取值：
    /// <list type="bullet">
    /// <item><description>fill：卡片最大支持宽度</description></item>
    /// <item><description>auto：自适应宽度</description></item>
    /// <item><description>[16,999]px：自定义宽度，如 "20px"</description></item>
    /// </list>
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 交互容器的高度。可取值：
    /// <list type="bullet">
    /// <item><description>auto：自适应高度</description></item>
    /// <item><description>[10,999]px：自定义高度，如 "20px"</description></item>
    /// </list>
    /// </summary>
    public string? Height { get; set; }

    /// <summary>
    /// 容器内组件的排列方向。可选值：
    /// <list type="bullet">
    /// <item><description>vertical：垂直排列</description></item>
    /// <item><description>horizontal：水平排列</description></item>
    /// </list>
    /// </summary>
    public string? Direction { get; set; }

    /// <summary>
    /// 容器内组件的水平间距。可选值：
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
    /// 容器内组件水平对齐的方式。可取值：
    /// <list type="bullet">
    /// <item><description>left：左对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>right：右对齐</description></item>
    /// </list>
    /// </summary>
    public string? HorizontalAlign { get; set; }

    /// <summary>
    /// 容器内组件的垂直间距。可选值同 HorizontalSpacing。
    /// </summary>
    public string? VerticalSpacing { get; set; }

    /// <summary>
    /// 容器内组件垂直对齐的方式。可取值：
    /// <list type="bullet">
    /// <item><description>top：上对齐</description></item>
    /// <item><description>center：居中对齐</description></item>
    /// <item><description>bottom：下对齐</description></item>
    /// </list>
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 交互容器的背景色样式。可取值：
    /// <list type="bullet">
    /// <item><description>default：默认的白底样式</description></item>
    /// <item><description>laser：镭射渐变彩色样式</description></item>
    /// <item><description>卡片支持的颜色枚举值和 RGBA 语法自定义颜色</description></item>
    /// </list>
    /// </summary>
    public string? BackgroundStyle { get; set; }

    /// <summary>
    /// 容器的内边距。取值范围 [-99,99]px。
    /// </summary>
    public string? Padding { get; set; }

    /// <summary>
    /// 是否展示边框，粗细固定为 1px。
    /// </summary>
    public bool? HasBorder { get; set; }

    /// <summary>
    /// 边框的颜色，仅 has_border 为 true 时生效。
    /// </summary>
    public string? BorderColor { get; set; }

    /// <summary>
    /// 交互容器的圆角半径。可取值 [0,∞]px 或 [0,100]%。
    /// </summary>
    public string? CornerRadius { get; set; }

    /// <summary>
    /// 设置点击交互容器时的交互配置。如果交互容器内有交互组件，则优先响应交互组件定义的交互。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 是否禁用交互容器。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 禁用交互容器后，用户触发交互时的弹窗文案提醒。默认为空，即不弹窗。
    /// </summary>
    public TextElement? DisabledTips { get; set; }

    /// <summary>
    /// 用户在 PC 端将光标悬浮在交互容器上方时的文案提醒。默认为空。
    /// </summary>
    public TextElement? HoverTips { get; set; }

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }

    /// <summary>
    /// 交互容器内嵌的组件。支持除表单容器（form）和表格组件（table）外的其它所有组件。
    /// </summary>
    public List<CardElement> Elements { get; set; } = [];
}

// ──────────────── 折叠面板 (collapsible_panel) ────────────────

/// <summary>
/// 折叠面板 — 允许折叠次要信息（如备注、较长文本等），以突出主要信息。
/// <para>不能包含 form。暂不支持表格（table）组件。</para>
/// </summary>
public record CollapsiblePanelElement : CardElement
{
    /// <summary>
    /// 面板是否展开。可取值：
    /// <list type="bullet">
    /// <item><description>true：面板为展开状态</description></item>
    /// <item><description>false：面板为折叠状态（默认）</description></item>
    /// </list>
    /// </summary>
    public bool? Expanded { get; set; }

    /// <summary>
    /// 容器内组件的水平间距。可选值：
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
    /// 容器内组件水平对齐的方式。可取值：left / center / right。
    /// </summary>
    public string? HorizontalAlign { get; set; }

    /// <summary>
    /// 容器内组件的垂直间距。可选值同 HorizontalSpacing。
    /// </summary>
    public string? VerticalSpacing { get; set; }

    /// <summary>
    /// 容器内组件垂直对齐的方式。可取值：top / center / bottom。
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 容器的排列方向。可选值：vertical / horizontal。
    /// </summary>
    public string? Direction { get; set; }

    /// <summary>
    /// 容器的内边距。取值范围 [-99,99]px。
    /// </summary>
    public string? Padding { get; set; }

    /// <summary>
    /// 折叠面板的背景色，默认为透明。枚举值参见颜色枚举值。
    /// </summary>
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// 折叠面板的标题设置。
    /// </summary>
    public CollapsiblePanelHeader? Header { get; set; }

    /// <summary>
    /// 边框设置。默认不显示边框。
    /// </summary>
    public CollapsiblePanelBorder? Border { get; set; }

    /// <summary>
    /// 各个组件的 JSON 结构。暂不支持表单（form）组件。
    /// </summary>
    public List<CardElement> Elements { get; set; } = [];
}

/// <summary>
/// 折叠面板标题设置。
/// </summary>
public record CollapsiblePanelHeader
{
    /// <summary>
    /// 标题文本设置。tag 可取值：
    /// <list type="bullet">
    /// <item><description>plain_text：普通文本内容</description></item>
    /// <item><description>lark_md：富文本内容</description></item>
    /// </list>
    /// </summary>
    public TextElement? Title { get; set; }

    /// <summary>
    /// 标题图标。支持自定义或使用图标库中的图标。
    /// </summary>
    public CollapsiblePanelIcon? Icon { get; set; }

    /// <summary>
    /// 图标的位置。可选值：
    /// <list type="bullet">
    /// <item><description>left：图标在标题区域最左侧</description></item>
    /// <item><description>right：图标在标题区域最右侧</description></item>
    /// <item><description>follow_text：图标在文本右侧</description></item>
    /// </list>
    /// </summary>
    public string? IconPosition { get; set; }

    /// <summary>
    /// 折叠面板展开时图标旋转的角度。正值为顺时针，负值为逆时针。可选值：-180 / -90 / 90 / 180。
    /// </summary>
    public int? IconExpandedAngle { get; set; }

    /// <summary>
    /// 折叠面板标题区域的背景颜色设置，默认为透明色。
    /// <para><strong>注意</strong>：如未设置此字段，标题区域的背景色由折叠面板的 background_color 字段决定。</para>
    /// </summary>
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// 标题区域的垂直居中方式。可取值：top / center / bottom。
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 标题区域的内边距。取值范围 [-99,99]px。
    /// </summary>
    public string? Padding { get; set; }

    /// <summary>
    /// 标题元素的宽度。可取值：
    /// <list type="bullet">
    /// <item><description>fill：标题和折叠面板等宽</description></item>
    /// <item><description>auto：标题自适应文本长度</description></item>
    /// <item><description>auto_when_fold：仅在折叠面板收起后，标题自适应文本长度</description></item>
    /// </list>
    /// </summary>
    public string? Width { get; set; }
}

/// <summary>
/// 折叠面板图标配置。
/// </summary>
public record CollapsiblePanelIcon
{
    /// <summary>
    /// 图标标签，默认为 "standard_icon"。
    /// </summary>
    public string Tag { get; set; } = "standard_icon";

    /// <summary>
    /// 图标库中图标的 token。
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// 图标尺寸。
    /// </summary>
    public string? Size { get; set; }
}

/// <summary>
/// 折叠面板边框设置。
/// </summary>
public record CollapsiblePanelBorder
{
    /// <summary>
    /// 边框颜色。枚举值参见颜色枚举值。
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// 圆角设置。
    /// </summary>
    public string? CornerRadius { get; set; }
}
