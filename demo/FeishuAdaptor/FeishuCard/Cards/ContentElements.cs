namespace FeishuAdaptor.FeishuCard.Cards;
// ReSharper disable ClassNeverInstantiated.Global

// ──────────────── 普通文本 (div) ────────────────

/// <summary>
/// 普通文本组件 — 支持普通文本和 Markdown 文本（通过 text.tag 区分）。
/// </summary>
public record DivElement : CardElement
{
    /// <summary>
    /// 文本的宽度。可取值：
    /// <list type="bullet">
    /// <item><description>fill：文本的宽度将与组件宽度一致，撑满组件</description></item>
    /// <item><description>auto：文本的宽度自适应文本内容本身的长度</description></item>
    /// <item><description>[16,999]px：自定义文本宽度</description></item>
    /// </list>
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 配置卡片的普通文本信息。tag 可为 plain_text 或 lark_md。
    /// </summary>
    public TextElement? Text { get; set; }

    /// <summary>
    /// 添加图标作为文本前缀图标。支持自定义或使用图标库中的图标。
    /// </summary>
    public IconElement? Icon { get; set; }
}

// ──────────────── 富文本 (markdown) ────────────────

/// <summary>
/// 富文本/Markdown 组件 — 支持渲染文本、图片、分割线等。
/// <para>了解支持的 Markdown 语法，参考飞书文档。</para>
/// </summary>
public record MarkdownElement : CardElement
{
    /// <summary>
    /// 文本内容的对齐方式。可取值：left / center / right。
    /// </summary>
    public string? TextAlign { get; set; }

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
    /// Markdown 文本内容。支持加粗、斜体、链接、代码块等。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 添加图标作为文本前缀图标。支持自定义或使用图标库中的图标。
    /// </summary>
    public IconElement? Icon { get; set; }
}

// ──────────────── 图片 (img) ────────────────

/// <summary>
/// 图片组件 — 通过 img_key 展示图片。
/// <para>img_key 通过上传图片接口获取：https://open.feishu.cn/document/server-docs/im-v1/image/create</para>
/// </summary>
public record ImgElement : CardElement
{
    /// <summary>
    /// 通过上传图片接口获取的 img_key。
    /// </summary>
    public string? ImgKey { get; set; }

    /// <summary>
    /// 悬浮（hover）在图片上时展示的说明文案。
    /// </summary>
    public TextElement? Alt { get; set; }

    /// <summary>
    /// 图片标题。
    /// </summary>
    public TextElement? Title { get; set; }

    /// <summary>
    /// 是否为透明底色。默认为 false，即图片为白色底色。
    /// </summary>
    public bool? Transparent { get; set; }

    /// <summary>
    /// 点击后是否放大图片。
    /// <list type="bullet">
    /// <item><description>true：点击图片后，弹出图片查看器放大查看</description></item>
    /// <item><description>false：点击图片后，响应卡片本身的交互事件</description></item>
    /// </list>
    /// </summary>
    public bool? Preview { get; set; }

    /// <summary>
    /// 图片的圆角半径。可取值 [0,∞]px 或 [0,100]%。
    /// </summary>
    public string? CornerRadius { get; set; }

    /// <summary>
    /// 图片的裁剪模式。可取值：
    /// <list type="bullet">
    /// <item><description>crop_center：居中裁剪</description></item>
    /// <item><description>crop_top：顶部裁剪</description></item>
    /// <item><description>fit_horizontal：完整展示不裁剪</description></item>
    /// </list>
    /// </summary>
    public string? ScaleType { get; set; }

    /// <summary>
    /// 图片尺寸。仅在 scale_type 为 crop_center 或 crop_top 时生效。可取值：
    /// <list type="bullet">
    /// <item><description>stretch：超大图，适用于高宽比小于 16:9 的图片</description></item>
    /// <item><description>large：大图（160×160）</description></item>
    /// <item><description>medium：中图（80×80）</description></item>
    /// <item><description>small：小图（40×40）</description></item>
    /// <item><description>tiny：超小图（16×16）</description></item>
    /// <item><description>[1,1000]px [1,1000]px：自定义尺寸</description></item>
    /// </list>
    /// </summary>
    public string? Size { get; set; }
}

// ──────────────── 多图混排 (img_combination) ────────────────

/// <summary>
/// 多图混排组件 — 支持双图、三图、二等分、三等分布局。
/// </summary>
public record ImgCombinationElement : CardElement
{
    /// <summary>
    /// 多图混排的方式。可取值：
    /// <list type="bullet">
    /// <item><description>double：双图混排，最多两张图</description></item>
    /// <item><description>triple：三图混排，最多三张图</description></item>
    /// <item><description>bisect：等分双列图混排，每行两个等大正方形图，最多六张图</description></item>
    /// <item><description>trisect：等分三列图混排，每行三个等大正方形图，最多九张图</description></item>
    /// </list>
    /// </summary>
    public string? CombinationMode { get; set; }

    /// <summary>
    /// 是否为透明底色。默认为 false，即图片为白色底色。
    /// </summary>
    public bool? CombinationTransparent { get; set; }

    /// <summary>
    /// 多图混排图片的圆角半径。可取值 [0,∞]px 或 [0,100]%。
    /// </summary>
    public string? CornerRadius { get; set; }

    /// <summary>
    /// 图片资源的 img_key 数组，顺序与图片排列顺序一致。
    /// </summary>
    public List<CombinationImage> ImgList { get; set; } = [];
}

/// <summary>
/// 多图混排中的单张图片。
/// </summary>
public record CombinationImage
{
    /// <summary>
    /// 通过上传图片接口获取的 img_key。
    /// </summary>
    public string? ImgKey { get; set; }
}

// ──────────────── 分割线 (hr) ────────────────

/// <summary>
/// 分割线组件 — 无额外属性。
/// </summary>
public record HrElement : CardElement;

// ──────────────── 表格 (table) ────────────────

/// <summary>
/// 表格组件 — 只能出现在卡片根级别，不能嵌套。
/// <para>每张卡片最多 5 个表格，最多 50 列。</para>
/// </summary>
public record TableElement : CardElement
{
    /// <summary>
    /// 每页最大展示的数据行数。取值范围 [1,10] 整数。
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// 表格的行高。可取值：
    /// <list type="bullet">
    /// <item><description>low：低</description></item>
    /// <item><description>medium：中</description></item>
    /// <item><description>high：高</description></item>
    /// <item><description>auto：行高与自适应内容（V7.33+）</description></item>
    /// <item><description>[32,124]px：自定义行高</description></item>
    /// </list>
    /// </summary>
    public string? RowHeight { get; set; }

    /// <summary>
    /// 当 row_height 为 auto 时的最大行高。取值范围 [32,999]px。
    /// </summary>
    public string? RowMaxHeight { get; set; }

    /// <summary>
    /// 是否冻结首列。冻结后左右滚动时不滚动首列。
    /// </summary>
    public bool? FreezeFirstColumn { get; set; }

    /// <summary>
    /// 表头样式设置。
    /// </summary>
    public TableHeaderStyle? HeaderStyle { get; set; }

    /// <summary>
    /// 列定义数组。
    /// </summary>
    public List<TableColumn> Columns { get; set; } = [];

    /// <summary>
    /// 行数据数组。Dictionary 的 key 对应 column.name。
    /// </summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
}

/// <summary>
/// 表头样式设置。
/// </summary>
public record TableHeaderStyle
{
    /// <summary>
    /// 表头文本对齐方式。可取值：left / center / right。
    /// </summary>
    public string? TextAlign { get; set; }

    /// <summary>
    /// 表头文本大小。
    /// </summary>
    public string? TextSize { get; set; }

    /// <summary>
    /// 表头背景色样式。
    /// </summary>
    public string? BackgroundStyle { get; set; }

    /// <summary>
    /// 表头文本颜色。
    /// </summary>
    public string? TextColor { get; set; }

    /// <summary>
    /// 表头文本是否加粗。
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// 表头文本最大显示行数。
    /// </summary>
    public int? Lines { get; set; }
}

/// <summary>
/// 表格列定义。
/// </summary>
public record TableColumn
{
    /// <summary>
    /// 列标识，对应行数据中的 key。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 列标题。
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 列宽度。可取值："auto" / 具体值如 "200px"。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 列数据类型。可取值：
    /// <list type="bullet">
    /// <item><description>text：纯文本</description></item>
    /// <item><description>lark_md：Markdown 文本</description></item>
    /// <item><description>number：数字</description></item>
    /// <item><description>options：选项</description></item>
    /// <item><description>persons：人员</description></item>
    /// <item><description>date：日期</description></item>
    /// <item><description>markdown：Markdown（与 lark_md 等价）</description></item>
    /// </list>
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>
    /// 数字列的格式化配置。
    /// </summary>
    public TableColumnFormat? Format { get; set; }

    /// <summary>
    /// 日期列的格式化模板。
    /// </summary>
    public string? DateFormat { get; set; }

    /// <summary>
    /// 列内容的垂直对齐方式。可取值：top / center / bottom。
    /// </summary>
    public string? VerticalAlign { get; set; }

    /// <summary>
    /// 列内容的水平对齐方式。可取值：left / center / right。
    /// </summary>
    public string? HorizontalAlign { get; set; }
}

/// <summary>
/// 数字列格式化配置。
/// </summary>
public record TableColumnFormat
{
    /// <summary>
    /// 小数精度。
    /// </summary>
    public int? Precision { get; set; }

    /// <summary>
    /// 数字符号，如 "%"、"¥"。
    /// </summary>
    public string? Symbol { get; set; }

    /// <summary>
    /// 千分位分隔符。
    /// </summary>
    public string? Separator { get; set; }
}

// ──────────────── 图表 (chart) ────────────────

/// <summary>
/// 图表组件 — 基于 VChart，支持折线图、面积图、柱状图、饼图、词云等。
/// </summary>
public record ChartElement : CardElement
{
    /// <summary>
    /// 基于 VChart 的图表定义对象。
    /// </summary>
    public object? ChartSpec { get; set; }

    /// <summary>
    /// 图表的宽高比。可取值：1:1 / 2:1 / 4:3 / 16:9。
    /// PC 端默认 16:9，移动端默认 1:1。
    /// </summary>
    public string? AspectRatio { get; set; }

    /// <summary>
    /// 图表的主题样式。当图表内存在多个颜色时可使用此字段调整颜色样式。
    /// 可取值：brand / rainbow / complementary / converse / primary。
    /// <para><strong>注意</strong>：若在 chart_spec 中声明了样式类属性，此字段无效。</para>
    /// </summary>
    public string? ColorTheme { get; set; }

    /// <summary>
    /// 图表是否可在独立窗口查看。
    /// <list type="bullet">
    /// <item><description>true（默认）：PC 端可在独立窗口查看，移动端点击后全屏查看</description></item>
    /// <item><description>false：不支持独立查看</description></item>
    /// </list>
    /// </summary>
    public bool? Preview { get; set; }

    /// <summary>
    /// 图表组件的高度。可取值："auto"（根据宽高比自动计算）/ [1,999]px（自定义高度，此时宽高比失效）。
    /// </summary>
    public string? Height { get; set; }
}

// ──────────────── 人员 (person) ────────────────

/// <summary>
/// 人员组件 — 展示单个人员的用户名和头像。
/// </summary>
public record PersonElement : CardElement
{
    /// <summary>
    /// 人员的 ID。可取值：
    /// <list type="bullet">
    /// <item><description>Open ID：标识用户在某个应用中的身份</description></item>
    /// <item><description>Union ID：标识用户在某个应用开发商下的身份</description></item>
    /// <item><description>User ID：标识用户在某个租户内的身份</description></item>
    /// </list>
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 人员的头像尺寸。可取值：extra_small / small / medium / large。
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// 是否展示人员的头像。
    /// </summary>
    public bool? ShowAvatar { get; set; }

    /// <summary>
    /// 是否展示人员的用户名。
    /// </summary>
    public bool? ShowName { get; set; }

    /// <summary>
    /// 人员组件的展示样式。可选值：
    /// <list type="bullet">
    /// <item><description>normal：默认样式</description></item>
    /// <item><description>capsule：胶囊样式</description></item>
    /// </list>
    /// </summary>
    public string? Style { get; set; }
}

// ──────────────── 人员列表 (person_list) ────────────────

/// <summary>
/// 人员列表组件 — 展示多个人员的用户名和头像。
/// </summary>
public record PersonListElement : CardElement
{
    /// <summary>
    /// 人员列表。
    /// </summary>
    public List<PersonItem> Persons { get; set; } = [];

    /// <summary>
    /// 人员的头像尺寸。可取值：extra_small / small / medium / large。
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// 是否展示人员的用户名。
    /// </summary>
    public bool? ShowName { get; set; }

    /// <summary>
    /// 是否展示人员的头像。
    /// </summary>
    public bool? ShowAvatar { get; set; }

    /// <summary>
    /// 最大显示行数。不可为 0。
    /// </summary>
    public int? Lines { get; set; }

    /// <summary>
    /// 当人员列表中有无效用户 ID 时，是否忽略无效 ID。
    /// 默认 false，表示若存在无效用户 ID 将报错并返回无效的用户 ID 列表。
    /// </summary>
    public string? DropInvalidUserId { get; set; }

    /// <summary>
    /// 前缀图标配置。
    /// </summary>
    public IconElement? Icon { get; set; }
}

/// <summary>
/// 人员列表中的人员信息。
/// </summary>
public record PersonItem
{
    /// <summary>
    /// 人员的 ID。可以是 Open ID、Union ID 或 User ID。
    /// </summary>
    public string? Id { get; set; }
}
