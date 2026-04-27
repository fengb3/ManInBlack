using System.Text;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Tools;

namespace ManInBlack.AI.ToolCallFilters;

// [ServiceRegister.Scoped]
// public partial class LargeResultFilter(IUserWorkspace workspace) : ToolCallFilter
// {
//     const int MaxResultSize = 3 * 1024;
//
//     public override async Task ExecuteAsync(
//         ToolExecuteContext context,
//         Func<ToolExecuteContext, Task> next
//     )
//     {
//         await next(context);
//
//         if (context.Error is not null)
//             return;
//
//         var result = context.Result?.ToString();
//         if (string.IsNullOrEmpty(result) || Encoding.UTF8.GetByteCount(result) <= MaxResultSize)
//             return;
//
//         var fileName =
//             $"{context.ToolName}_{context.CallId}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.txt";
//         var filePath = Path.Combine(workspace.UserRoot, "large-results", fileName);
//         Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
//         await File.WriteAllTextAsync(filePath, result);
//
//         var truncated = result[..MaxResultSize];
//         context.Result = $"""
//             <truncated file="{fileName}" size="{Encoding.UTF8.GetByteCount(
//                 result
//             )}" notes="use ReadFile tool to access the full content">
//             {truncated}
//             </truncated>
//             """;
//     }
// }
