using System;
using System.IO;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Tests.Helpers;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.Options;
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
        var resolver = new FileAccessPolicyResolver(workspace, Options.Create(new ManInBlackSettings()));
        _tools = new FileTools(resolver);
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

    #region Read 隔离测试

    [Fact]
    public async Task Read_workspace内_成功()
    {
        var filePath = Path.Combine(_workspaceDir, "readable.txt");
        File.WriteAllText(filePath, "hello");

        var content = await _tools.Read(filePath);

        Assert.Equal("hello", content);
    }

    [Fact]
    public async Task Read_临时目录内_成功()
    {
        var filePath = Path.Combine(_tempDir, "tmp.txt");
        File.WriteAllText(filePath, "tmpdata");

        var content = await _tools.Read(filePath);

        Assert.Equal("tmpdata", content);
    }

    [Fact]
    public async Task Read_允许列表外_拒绝()
    {
        var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "outside.txt");
        File.WriteAllText(outside, "secret");
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _tools.Read(outside));
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Fact]
    public void Glob_允许列表外的根_拒绝()
    {
        var outsideDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "glob_outside_dir");
        Directory.CreateDirectory(outsideDir);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _tools.Glob("*.txt", outsideDir));
        }
        finally
        {
            if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, true);
        }
    }

    [Fact]
    public void Glob_workspace内_返回结果()
    {
        var inside = Path.Combine(_workspaceDir, "a.txt");
        File.WriteAllText(inside, "x");

        var result = _tools.Glob("*.txt", _workspaceDir);

        Assert.Contains(inside, result);
    }

    #endregion
}
