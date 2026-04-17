namespace FeishuAdaptor.FeishuCard.Cards;
// ReSharper disable ClassNeverInstantiated.Global

// ──────────────── 按钮 (button) ────────────────

/// <summary>
/// 按钮组件 — 提供回传交互或链接跳转能力。
/// </summary>
public record ButtonElement : CardElement
{
    /// <summary>
    /// 按钮上的文本。
    /// </summary>
    public TextElement? Text { get; set; }

    /// <summary>
    /// 按钮类型。可取值：
    /// <list type="bullet">
    /// <item><description>default：默认按钮</description></item>
    /// <item><description>primary：主要按钮</description></item>
    /// <item><description>danger：危险按钮</description></item>
    /// <item><description>text：纯文本按钮</description></item>
    /// <item><description>primary_text / danger_text：文本样式</description></item>
    /// <item><description>primary_filled / danger_filled：填充样式</description></item>
    /// <item><description>laser：镭射样式</description></item>
    /// </list>
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 按钮尺寸。可取值：tiny / small / medium / large。
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// 按钮宽度。可取值："default" / "fill" / 具体值如 "200px"。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 按钮图标。
    /// </summary>
    public IconElement? Icon { get; set; }

    /// <summary>
    /// 是否禁用按钮。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 禁用按钮后，用户点击时的弹窗文案。
    /// </summary>
    public TextElement? DisabledTips { get; set; }

    /// <summary>
    /// 用户在 PC 端将光标悬浮在按钮上时的文案提醒。
    /// </summary>
    public TextElement? HoverTips { get; set; }

    /// <summary>
    /// 按钮的交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }

    /// <summary>
    /// 表单内使用时的组件标识。在表单容器内必填且需在卡片全局内唯一。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 表单内的按钮交互类型。可取值：
    /// <list type="bullet">
    /// <item><description>submit：绑定提交事件，将触发表单容器的提交事件</description></item>
    /// <item><description>reset：绑定取消提交事件，重置所有表单组件的输入值为初始值</description></item>
    /// </list>
    /// </summary>
    public string? FormActionType { get; set; }
}

// ──────────────── 输入框 (input) ────────────────

/// <summary>
/// 输入框组件 — 支持收集文本内容，如原因、评价、备注等。
/// </summary>
public record InputElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。在表单容器内必填且需在卡片全局内唯一。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 输入框占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 输入框默认值。
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// 是否禁用输入框。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 禁用输入框后，用户点击时的弹窗文案。
    /// </summary>
    public TextElement? DisabledTips { get; set; }

    /// <summary>
    /// 输入框宽度。可取值："default" / "fill" / 具体值。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 最大输入字符数。
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// 输入框类型。可取值：
    /// <list type="bullet">
    /// <item><description>text：单行文本</description></item>
    /// <item><description>multiline_text：多行文本</description></item>
    /// <item><description>password：密码输入</description></item>
    /// </list>
    /// </summary>
    public string? InputType { get; set; }

    /// <summary>
    /// 是否在输入框前展示图标。
    /// </summary>
    public bool? ShowIcon { get; set; }

    /// <summary>
    /// 多行文本的可见行数。
    /// </summary>
    public int? Rows { get; set; }

    /// <summary>
    /// 多行文本是否自动调整高度。
    /// </summary>
    public bool? AutoResize { get; set; }

    /// <summary>
    /// 多行文本自动调整高度时的最大行数。
    /// </summary>
    public int? MaxRows { get; set; }

    /// <summary>
    /// 输入框标签文本。
    /// </summary>
    public TextElement? Label { get; set; }

    /// <summary>
    /// 标签位置。可取值："top" / "left"。
    /// </summary>
    public string? LabelPosition { get; set; }

    /// <summary>
    /// 输入框的值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 输入框的交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 下拉选择-单选 (select_static) ────────────────

/// <summary>
/// 下拉单选组件 — 支持自定义选项文本、图标和回传参数。
/// </summary>
public record SelectStaticElement : CardElement
{
    /// <summary>
    /// 组件类型。可取值："default" / "text"。
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 选项列表。
    /// </summary>
    public List<SelectOption> Options { get; set; } = [];

    /// <summary>
    /// 初始选中的选项值。
    /// </summary>
    public string? InitialOption { get; set; }

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

/// <summary>
/// 下拉选择选项。
/// </summary>
public record SelectOption
{
    /// <summary>
    /// 选项文本。
    /// </summary>
    public TextElement? Text { get; set; }

    /// <summary>
    /// 选项图标。
    /// </summary>
    public IconElement? Icon { get; set; }

    /// <summary>
    /// 选项的回传值。
    /// </summary>
    public string? Value { get; set; }
}

// ──────────────── 下拉选择-多选 (multi_select_static) ────────────────

/// <summary>
/// 下拉多选组件 — 必须放在 form 容器内。
/// </summary>
public record MultiSelectStaticElement : CardElement
{
    /// <summary>
    /// 组件类型。
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 选项列表。
    /// </summary>
    public List<SelectOption> Options { get; set; } = [];

    /// <summary>
    /// 初始选中的选项值列表。
    /// </summary>
    public List<string>? SelectedValues { get; set; }

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 人员选择-单选 (select_person) ────────────────

/// <summary>
/// 人员单选组件 — 支持添加指定人员作为单选选项。
/// </summary>
public record SelectPersonElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 人员选项列表。
    /// </summary>
    public List<PersonOption> Options { get; set; } = [];

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

/// <summary>
/// 人员选项。
/// </summary>
public record PersonOption
{
    /// <summary>
    /// 用户的 open_id / user_id / union_id。
    /// </summary>
    public string? Value { get; set; }
}

// ──────────────── 人员选择-多选 (multi_select_person) ────────────────

/// <summary>
/// 人员多选组件 — 必须放在 form 容器内。
/// </summary>
public record MultiSelectPersonElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 人员选项列表。
    /// </summary>
    public List<PersonOption> Options { get; set; } = [];

    /// <summary>
    /// 初始选中的人员值列表。
    /// </summary>
    public List<string>? SelectedValues { get; set; }

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 日期选择器 (date_picker) ────────────────

/// <summary>
/// 日期选择器组件 — 支持选择日期。
/// </summary>
public record DatePickerElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 初始日期，格式 YYYY-MM-DD。
    /// </summary>
    public string? InitialDate { get; set; }

    /// <summary>
    /// 当前选中值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 时间选择器 (picker_time) ────────────────

/// <summary>
/// 时间选择器组件 — 支持选择时间。
/// </summary>
public record PickerTimeElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 初始时间，格式 HH:mm。
    /// </summary>
    public string? InitialTime { get; set; }

    /// <summary>
    /// 当前选中值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 日期时间选择器 (picker_datetime) ────────────────

/// <summary>
/// 日期时间选择器组件 — 支持选择日期和时间。
/// </summary>
public record PickerDatetimeElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 初始日期时间，格式 YYYY-MM-DD HH:mm。
    /// </summary>
    public string? InitialDatetime { get; set; }

    /// <summary>
    /// 当前选中值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

// ──────────────── 图片选择 (select_img) ────────────────

/// <summary>
/// 图片选择组件 — 支持单选/多选图片，仅支持 JSON 代码构建。
/// </summary>
public record SelectImgElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表单容器内组件是否必填。
    /// </summary>
    public bool? Required { get; set; }

    /// <summary>
    /// 是否禁用组件。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 占位文本。
    /// </summary>
    public TextElement? Placeholder { get; set; }

    /// <summary>
    /// 组件宽度。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 是否支持多选。
    /// </summary>
    public bool? MultiSelect { get; set; }

    /// <summary>
    /// 图片布局方式。可取值："stretch" / "bisect" / "trisect"。
    /// </summary>
    public string? Layout { get; set; }

    /// <summary>
    /// 图片宽高比。可取值："1:1" / "4:3" / "16:9"。
    /// </summary>
    public string? AspectRatio { get; set; }

    /// <summary>
    /// 是否可预览图片。
    /// </summary>
    public bool? CanPreview { get; set; }

    /// <summary>
    /// 图片选项列表。
    /// </summary>
    public List<ImageOption> Options { get; set; } = [];

    /// <summary>
    /// 禁用后，用户点击时的弹窗文案。
    /// </summary>
    public TextElement? DisabledTips { get; set; }

    /// <summary>
    /// 当前选中值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

/// <summary>
/// 图片选择选项。
/// </summary>
public record ImageOption
{
    /// <summary>
    /// 通过上传图片接口获取的 img_key。
    /// </summary>
    public string? ImgKey { get; set; }

    /// <summary>
    /// 选项的回传值。
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// 是否禁用该选项。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 禁用后，用户点击时的弹窗文案。
    /// </summary>
    public TextElement? DisabledTips { get; set; }

    /// <summary>
    /// 用户在 PC 端将光标悬浮时的文案提醒。
    /// </summary>
    public TextElement? HoverTips { get; set; }
}

// ──────────────── 折叠按钮组 (overflow) ────────────────

/// <summary>
/// 折叠按钮组 — 默认折叠，点击展示所有按钮。
/// </summary>
public record OverflowElement : CardElement
{
    /// <summary>
    /// 组件宽度。可取值："default" / "fill" / 具体值。
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// 折叠按钮选项列表。
    /// </summary>
    public List<OverflowOption> Options { get; set; } = [];

    /// <summary>
    /// 当前选中值。
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }
}

/// <summary>
/// 折叠按钮选项。
/// </summary>
public record OverflowOption
{
    /// <summary>
    /// 选项文本。
    /// </summary>
    public TextElement? Text { get; set; }

    /// <summary>
    /// 多平台链接配置。
    /// </summary>
    public MultiUrl? MultiUrl { get; set; }

    /// <summary>
    /// 选项的回传值。
    /// </summary>
    public string? Value { get; set; }
}

// ──────────────── 勾选器 (checker) ────────────────

/// <summary>
/// 勾选器组件 — 主要用于任务勾选场景，仅支持 JSON 代码构建。
/// </summary>
public record CheckerElement : CardElement
{
    /// <summary>
    /// 表单内使用时的组件标识。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 是否已勾选。
    /// </summary>
    public bool? Checked { get; set; }

    /// <summary>
    /// 是否禁用勾选器。
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>
    /// 勾选器文本。
    /// </summary>
    public TextElement? Text { get; set; }

    /// <summary>
    /// 整个区域是否可点击触发勾选。
    /// </summary>
    public bool? OverallCheckable { get; set; }

    /// <summary>
    /// 右侧按钮区域配置。
    /// </summary>
    public CheckerButtonArea? ButtonArea { get; set; }

    /// <summary>
    /// 勾选后的样式配置。
    /// </summary>
    public CheckerCheckedStyle? CheckedStyle { get; set; }

    /// <summary>
    /// 内边距。
    /// </summary>
    public string? Padding { get; set; }

    /// <summary>
    /// 二次确认弹窗配置。
    /// </summary>
    public ConfirmElement? Confirm { get; set; }

    /// <summary>
    /// 交互行为配置。
    /// </summary>
    public List<ActionBehavior> Behaviors { get; set; } = [];

    /// <summary>
    /// 用户在 PC 端将光标悬浮时的文案提醒。
    /// </summary>
    public TextElement? HoverTips { get; set; }

    /// <summary>
    /// 禁用后，用户点击时的弹窗文案。
    /// </summary>
    public TextElement? DisabledTips { get; set; }
}

/// <summary>
/// 勾选器右侧按钮区域配置。
/// </summary>
public record CheckerButtonArea
{
    /// <summary>
    /// PC 端按钮的显示规则。可取值：
    /// <list type="bullet">
    /// <item><description>always：始终显示</description></item>
    /// <item><description>hover：悬浮时显示</description></item>
    /// </list>
    /// </summary>
    public string? PcDisplayRule { get; set; }

    /// <summary>
    /// 按钮列表。
    /// </summary>
    public List<ButtonElement> Buttons { get; set; } = [];
}

/// <summary>
/// 勾选后的样式配置。
/// </summary>
public record CheckerCheckedStyle
{
    /// <summary>
    /// 是否显示删除线。
    /// </summary>
    public bool? ShowStrikethrough { get; set; }

    /// <summary>
    /// 透明度。
    /// </summary>
    public double? Opacity { get; set; }
}
