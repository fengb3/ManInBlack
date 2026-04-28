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

    private const string OutOfWorkspaceError = "Error: 不允许在工作空间外修改、创建或删除文件。你只能修改工作空间内的文件。";

    /// <summary>
    /// Reads a file and returns its content. Supports reading the entire file or a specific range of lines.
    /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    /// </summary>
    /// <param name="filePath">Path to the file. Can be absolute or relative to the workspace root.</param>
    /// <param name="offset">Line number to start reading from (0-indexed). Defaults to 0.</param>
    /// <param name="length">Number of lines to read. -1 (default) reads from offset to end of file.</param>
    /// <returns>The file content as a string, with lines joined by newline characters.</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public async Task<string> ReadFile(string filePath, int offset = 0, int length = -1)
    {
        filePath = ResolvePath(filePath);
        if (!File.Exists(filePath))
            return $"Error: File not found: {filePath}";
        if (offset < 0)
            return "Error: Offset must be non-negative";
        if (length < -1)
            return "Error: Length must be -1 (for all lines) or a non-negative integer";

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
            return $"Error: Offset {offset} exceeds file length ({lineIndex} lines)";

        return string.Join(Environment.NewLine, selectedLines);
    }

    /// <summary>
    /// Creates a new file or completely overwrites an existing file with the given content.
    /// Parent directories are created automatically if they do not exist.
    /// 只能在工作空间内创建或覆盖文件，不允许在工作空间外写入。
    /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    /// </summary>
    /// <param name="filePath">Path to the file. Can be absolute or relative to the workspace root.</param>
    /// <param name="content">The complete content to write to the file.</param>
    /// <returns>A confirmation message indicating the file was written.</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string WriteFile(string filePath, string content)
    {
        filePath = ResolvePath(filePath);
        if (!IsInsideWorkspace(filePath))
            return $"{OutOfWorkspaceError} Path: {filePath}";
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, content);
        return $"File written: {filePath}";

    }
    /// <summary>
    /// Performs an exact string replacement in an existing file.
    /// Finds the first occurrence of originalContent and replaces it with newContent.
    /// If originalContent is not found, the update is aborted to prevent data loss.
    /// You must read the file with ReadFile before calling this tool to ensure you have the current content.
    /// 只能在工作空间内修改文件，不允许修改工作空间外的文件。
    /// All file paths are relative to the workspace root directory. Relative paths are resolved automatically.
    /// </summary>
    /// <param name="filePath">Path to the file. Can be absolute or relative to the workspace root.</param>
    /// <param name="originalContent">The exact text to find and replace. Must match the current file content exactly.</param>
    /// <param name="newContent">The text to replace originalContent with.</param>
    /// <returns>A confirmation message on success, or an error message if the original content was not found.</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string UpdateFile(string filePath, string originalContent, string newContent)
    {
        filePath = ResolvePath(filePath);
        if (!IsInsideWorkspace(filePath))
            return $"{OutOfWorkspaceError} Path: {filePath}";
        if (!File.Exists(filePath))
            return $"File not found: {filePath}";

        var currentContent = File.ReadAllText(filePath);

        if (!currentContent.Contains(originalContent))
            return "Update aborted: the file has been modified since it was last read. Please re-read the file and try again.\n" + $"File: {filePath}";

        File.WriteAllText(filePath, currentContent.Replace(originalContent, newContent));
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
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string Glob(string pattern, string? directory = null)
    {
        var searchDir = directory is null ? _userWorkspace : ResolvePath(directory);
        if (!Directory.Exists(searchDir))
            return $"Directory not found: {searchDir}";

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
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string Grep(string pattern, string? directory = null, string glob = "*")
    {
        var searchDir = directory is null ? _userWorkspace : ResolvePath(directory);
        if (!Directory.Exists(searchDir))
            return $"Directory not found: {searchDir}";

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
}