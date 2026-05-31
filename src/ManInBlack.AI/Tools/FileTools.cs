using System.Text.RegularExpressions;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace ManInBlack.AI.Tools;

[ServiceRegister.Scoped]
public partial class FileTools(IUserWorkspace workspace)
{
    private readonly string _userWorkspace = workspace.WorkingDirectory;
    private readonly string _tempDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 图片
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",
        // 音频
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a",
        // 视频
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        // 压缩包
        ".zip", ".tar", ".gz", ".rar", ".7z", ".bz2", ".xz", ".zst",
        // 可执行文件
        ".exe", ".dll", ".so", ".dylib", ".bin", ".msi",
        // 文档
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        // 数据库
        ".db", ".sqlite", ".sqlite3", ".mdb",
        // 字体
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        // 编译产物
        ".pyc", ".class", ".o", ".obj", ".pdb", ".nupkg",
    };

    private static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (BinaryExtensions.Contains(ext))
            return true;

        // 对未知扩展名，探测前 8KB 内容判断是否含 null 字节
        try
        {
            var probeSize = 8192;
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[probeSize];
            var bytesRead = stream.Read(buffer, 0, probeSize);
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }
        }
        catch
        {
            // 读取失败时不阻止，让后续逻辑处理
        }

        return false;
    }
    
    private string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(_userWorkspace, path));

    private bool IsInsideWorkspace(string resolvedPath)
    {
        var normalized = Path.GetFullPath(resolvedPath);
        var workspaceRoot = Path.GetFullPath(_userWorkspace);
        return normalized.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase) &&
               (normalized.Length == workspaceRoot.Length ||
                normalized[workspaceRoot.Length] == Path.DirectorySeparatorChar ||
                normalized[workspaceRoot.Length] == Path.AltDirectorySeparatorChar);
    }

    private bool IsInsideTempDirectory(string resolvedPath)
    {
        var normalized = Path.GetFullPath(resolvedPath);
        var tempRoot = Path.GetFullPath(_tempDirectory);
        return normalized.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
               (normalized.Length == tempRoot.Length ||
                normalized[tempRoot.Length] == Path.DirectorySeparatorChar ||
                normalized[tempRoot.Length] == Path.AltDirectorySeparatorChar);
    }

    private bool IsInsideAllowedDirectory(string resolvedPath)
    {
        // 根目录本身不允许操作（防止删除工作空间/临时目录根）
        if (IsExactPath(resolvedPath, _userWorkspace) || IsExactPath(resolvedPath, _tempDirectory))
            return false;

        return IsInsideWorkspace(resolvedPath) || IsInsideTempDirectory(resolvedPath);
    }

    private static bool IsExactPath(string resolvedPath, string root)
    {
        var normalized = Path.GetFullPath(resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalized, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private const string OutOfAllowedDirectoryError = "Error: 不允许在工作空间和临时目录外修改、创建或删除文件。你只能修改工作空间内或临时目录内的文件。";

    /// <summary>
    /// Reads a file and returns its content. Supports reading the entire file or a specific range of lines.
    /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    /// </summary>
    /// <param name="filePath">Path to the file. Can be absolute or relative to the workspace root.</param>
    /// <param name="offset">Line number to start reading from (0-indexed). Defaults to 0.</param>
    /// <param name="length">Number of lines to read. -1 (default) reads from offset to end of file.</param>
    /// <returns>The file content as a string, with lines joined by newline characters.</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public async Task<string> Read(string filePath, int offset = 0, int length = -1)
    {
        filePath = ResolvePath(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}", filePath);
        if (IsBinaryFile(filePath))
            throw new InvalidOperationException($"不支持读取二进制文件: {filePath}");
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset 必须为非负数");
        if (length < -1)
            throw new ArgumentOutOfRangeException(nameof(length), "Length 必须为 -1（读取全部行）或非负整数");

        var selectedLines = new List<string>();
        var lineIndex = 0;
        await foreach (var line in File.ReadLinesAsync(filePath))
        {
            if (lineIndex >= offset)
            {
                if (length != -1 && selectedLines.Count >= length)
                    break;
                selectedLines.Add(line);
            }
            lineIndex++;
        }

        if (offset >= lineIndex)
            throw new ArgumentOutOfRangeException(nameof(offset), $"Offset {offset} 超出文件行数 ({lineIndex} 行)");

        return string.Join(Environment.NewLine, selectedLines);
    }

    /// <summary>
    /// Creates a new file or completely overwrites an existing file with the given content.
    /// Parent directories are created automatically if they do not exist.
    /// 只能在工作空间或临时目录内创建或覆盖文件，不允许在其他位置写入。
    /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    /// </summary>
    /// <param name="filePath">Path to the file. Can be absolute or relative to the workspace root.</param>
    /// <param name="content">The complete content to write to the file.</param>
    /// <returns>A confirmation message indicating the file was written.</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string Write(string filePath, string content)
    {
        filePath = ResolvePath(filePath);
        if (!IsInsideAllowedDirectory(filePath))
            throw new UnauthorizedAccessException($"{OutOfAllowedDirectoryError} Path: {filePath}");
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, content);
        return $"File written: {filePath}";
    }
    
    /// <summary>
    /// 在已有文件中执行精确字符串替换。查找 originalContent 的首次出现并替换为 newContent。
    /// 如果未找到 originalContent，则中止操作以防止数据丢失。
    /// 调用此工具前必须先通过 Read 读取文件，确保拥有最新内容。
    /// 只能在工作空间或临时目录内修改文件，不允许修改其他位置的文件。
    /// 所有文件路径相对于工作空间根目录，相对路径会自动解析。
    /// </summary>
    /// <param name="filePath">文件路径。可以是绝对路径或相对于工作空间根目录的相对路径。</param>
    /// <param name="originalContent">要查找并替换的精确文本。必须与文件当前内容完全匹配。</param>
    /// <param name="newContent">用于替换 originalContent 的文本。</param>
    /// <returns>成功时返回确认消息；如果未找到原始内容则返回错误消息。</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string Edit(string filePath, string originalContent, string newContent)
    {
        filePath = ResolvePath(filePath);
        if (!IsInsideAllowedDirectory(filePath))
            throw new UnauthorizedAccessException($"{OutOfAllowedDirectoryError} Path: {filePath}");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"文件不存在: {filePath}", filePath);

        var currentContent = File.ReadAllText(filePath);

        var index = currentContent.IndexOf(originalContent, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException($"文件自上次读取后已被修改，中止操作。请重新读取文件后再试。\nFile: {filePath}");

        var updatedContent = string.Concat(
            currentContent.AsSpan(0, index),
            newContent,
            currentContent.AsSpan(index + originalContent.Length));
        File.WriteAllText(filePath, updatedContent);
        return $"File updated: {filePath}";
    }

    /// <summary>
    /// Searches for files matching a glob pattern. Returns matching file paths sorted by last modification time.
    /// Supports patterns like "**/*.cs", "src/**/*.tsx", or "*.json".
    /// Defaults to searching in the workspace root directory. Relative directory paths are resolved automatically.
    /// </summary>
    /// <param name="pattern">The glob pattern to match files against, e.g. "**/*.cs" or "src/**/*.tsx".</param>
    /// <param name="directory">The directory to search in. Can be absolute or relative to the workspace root. Defaults to workspace root.</param>
    /// <returns>The matching file paths, one per line, sorted by modification time (newest first).</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string Glob(string pattern, string? directory = null)
    {
        var searchDir = directory is null ? _userWorkspace : ResolvePath(directory);
        if (!Directory.Exists(searchDir))
            throw new DirectoryNotFoundException($"目录不存在: {searchDir}");

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);
        var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(searchDir)));
        var sorted = matchResult.Files
            .Select(f => Path.GetFullPath(Path.Combine(searchDir, f.Path)))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName);

        var result = string.Join(Environment.NewLine, sorted);
        return result.Length == 0 ? "No files matched the pattern." : result;
    }

    /// <summary>
    /// Searches file contents for lines matching a regular expression pattern.
    /// Returns the matching file paths along with the matched lines and their line numbers.
    /// Defaults to searching in the workspace root directory. Relative directory paths are resolved automatically.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to search for in file contents.</param>
    /// <param name="directory">The directory to search in. Can be absolute or relative to the workspace root. Defaults to workspace root.</param>
    /// <param name="glob">A glob pattern to filter which files to search, e.g. "*.cs" or "*.tsx". Defaults to "*" (all files).</param>
    /// <returns>The matching lines with file paths and line numbers.</returns>
    [AiTool]
    [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    public string Grep(string pattern, string? directory = null, string glob = "*")
    {
        var searchDir = directory is null ? _userWorkspace : ResolvePath(directory);
        if (!Directory.Exists(searchDir))
            throw new DirectoryNotFoundException($"目录不存在: {searchDir}");

        var regex = new Regex(pattern, RegexOptions.Compiled);
        var files = Directory.EnumerateFiles(searchDir, glob, SearchOption.AllDirectories);

        var results = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (regex.IsMatch(lines[i]))
                    results.Add($"{file}:{i + 1}: {lines[i]}");
            }
        }

        return results.Count == 0
            ? "No matches found."
            : string.Join(Environment.NewLine, results);
    }
    
    // delete operations are potentially destructive and may not be needed in many scenarios, so we can start without them and add later if necessary.

    // /// <summary>
    // /// Deletes a file at the specified path.
    // /// 只能在工作空间或临时目录内删除文件，不允许删除其他位置的文件。
    // /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    // /// </summary>
    // /// <param name="filePath">Path to the file to delete. Can be absolute or relative to the workspace root.</param>
    // /// <returns>A confirmation message on success, or an error message if the file was not found or access was denied.</returns>
    // [AiTool]
    // [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    // public string DeleteFile(string filePath)
    // {
    //     filePath = ResolvePath(filePath);
    //     if (!IsInsideAllowedDirectory(filePath))
    //         return $"{OutOfAllowedDirectoryError} Path: {filePath}";
    //     if (!File.Exists(filePath))
    //         return $"Error: File not found: {filePath}";
    //
    //     File.Delete(filePath);
    //     return $"File deleted: {filePath}";
    // }
    //
    // /// <summary>
    // /// Deletes a directory at the specified path, including all files and subdirectories.
    // /// 只能在工作空间或临时目录内删除目录，不允许删除其他位置的目录。
    // /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    // /// </summary>
    // /// <param name="directoryPath">Path to the directory to delete. Can be absolute or relative to the workspace root.</param>
    // /// <returns>A confirmation message on success, or an error message if the directory was not found or access was denied.</returns>
    // [AiTool]
    // [AiTool.HasFilter<AgentLifecycleFilter, LoggingFilter>]
    // public string DeleteDirectory(string directoryPath)
    // {
    //     directoryPath = ResolvePath(directoryPath);
    //     if (!IsInsideAllowedDirectory(directoryPath))
    //         return $"{OutOfAllowedDirectoryError} Path: {directoryPath}";
    //     if (!Directory.Exists(directoryPath))
    //         return $"Error: Directory not found: {directoryPath}";
    //
    //     Directory.Delete(directoryPath, recursive: true);
    //     return $"Directory deleted: {directoryPath}";
    // }
}