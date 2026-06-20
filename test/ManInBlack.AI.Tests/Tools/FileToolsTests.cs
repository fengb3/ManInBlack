using System;
using System.IO;
using ManInBlack.AI.Tests.Helpers;
using ManInBlack.AI.Tools;
using Xunit;

namespace ManInBlack.AI.Tests.Tools;

public class FileToolsTests : IDisposable
{
    private readonly string _workspaceDir;
    private readonly string _tempDir;
    private readonly FileTools _tools;

    public FileToolsTests()
    {
        // 创建独立的测试工作空间和临时目录，避免污染真实环境
        _workspaceDir = Path.Combine(Path.GetTempPath(), $"mib_test_ws_{Guid.NewGuid():N}");
        _tempDir = Path.Combine(Path.GetTempPath(), $"mib_test_tmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspaceDir);
        Directory.CreateDirectory(_tempDir);

        var workspace = new FakeUserWorkspace("test-user", _workspaceDir);
        _tools = new FileTools(workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceDir))
            Directory.Delete(_workspaceDir, true);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region WriteFile 临时目录测试

    [Fact]
    public void WriteFile_Temp目录内_写入成功()
    {
        var filePath = Path.Combine(_tempDir, "new-file.txt");

        var result = _tools.Write(filePath, "temp content");

        Assert.Equal($"File written: {filePath}", result);
        Assert.Equal("temp content", File.ReadAllText(filePath));
    }

    [Fact]
    public void WriteFile_Temp目录内_自动创建父目录()
    {
        var filePath = Path.Combine(_tempDir, "a", "b", "deep-file.txt");

        var result = _tools.Write(filePath, "deep content");

        Assert.Equal($"File written: {filePath}", result);
        Assert.Equal("deep content", File.ReadAllText(filePath));
    }

    [Fact]
    public void WriteFile_不允许的目录_拒绝()
    {
        var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "should-not-write.txt");

        // 安全检查抛 UnauthorizedAccessException（由框架 ToolExecutor 统一捕获为工具错误）
        var ex = Assert.Throws<UnauthorizedAccessException>(() => _tools.Write(filePath, "bad content"));
        Assert.Contains("不允许", ex.Message);
        Assert.False(File.Exists(filePath));
    }

    #endregion

    #region UpdateFile 临时目录测试

    [Fact]
    public void UpdateFile_Temp目录内_替换成功()
    {
        var filePath = Path.Combine(_tempDir, "update-target.txt");
        File.WriteAllText(filePath, "hello world");

        var result = _tools.Edit(filePath, "hello", "goodbye");

        Assert.Equal($"File updated: {filePath}", result);
        Assert.Equal("goodbye world", File.ReadAllText(filePath));
    }

    [Fact]
    public void UpdateFile_不允许的目录_拒绝()
    {
        var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "should-not-update.txt");

        var ex = Assert.Throws<UnauthorizedAccessException>(() => _tools.Edit(filePath, "old", "new"));
        Assert.Contains("不允许", ex.Message);
    }

    #endregion
}
