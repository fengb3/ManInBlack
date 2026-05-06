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

    // #region DeleteFile 测试
    //
    // [Fact]
    // public void DeleteFile_Workspace内_删除成功()
    // {
    //     var filePath = Path.Combine(_workspaceDir, "to-delete.txt");
    //     File.WriteAllText(filePath, "hello");
    //
    //     var result = _tools.DeleteFile(filePath);
    //
    //     Assert.Equal($"File deleted: {filePath}", result);
    //     Assert.False(File.Exists(filePath));
    // }
    //
    // [Fact]
    // public void DeleteFile_Temp目录内_删除成功()
    // {
    //     var filePath = Path.Combine(_tempDir, "to-delete.txt");
    //     File.WriteAllText(filePath, "hello");
    //
    //     var result = _tools.DeleteFile(filePath);
    //
    //     Assert.Equal($"File deleted: {filePath}", result);
    //     Assert.False(File.Exists(filePath));
    // }
    //
    // [Fact]
    // public void DeleteFile_文件不存在_返回错误()
    // {
    //     var filePath = Path.Combine(_workspaceDir, "nonexistent.txt");
    //
    //     var result = _tools.DeleteFile(filePath);
    //
    //     Assert.Contains("File not found", result);
    //     Assert.Contains(filePath, result);
    // }
    //
    // [Fact]
    // public void DeleteFile_不允许的目录_拒绝()
    // {
    //     var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "should-not-delete.txt");
    //
    //     var result = _tools.DeleteFile(filePath);
    //
    //     Assert.Contains("不允许", result);
    // }
    //
    // #endregion

    // #region DeleteDirectory 测试
    //
    // [Fact]
    // public void DeleteDirectory_Workspace内_递归删除成功()
    // {
    //     var dirPath = Path.Combine(_workspaceDir, "subdir");
    //     Directory.CreateDirectory(dirPath);
    //     File.WriteAllText(Path.Combine(dirPath, "file1.txt"), "content");
    //     Directory.CreateDirectory(Path.Combine(dirPath, "nested"));
    //     File.WriteAllText(Path.Combine(dirPath, "nested", "file2.txt"), "content2");
    //
    //     var result = _tools.DeleteDirectory(dirPath);
    //
    //     Assert.Equal($"Directory deleted: {dirPath}", result);
    //     Assert.False(Directory.Exists(dirPath));
    // }
    //
    // [Fact]
    // public void DeleteDirectory_Temp目录内_递归删除成功()
    // {
    //     var dirPath = Path.Combine(_tempDir, "subdir");
    //     Directory.CreateDirectory(dirPath);
    //     File.WriteAllText(Path.Combine(dirPath, "file.txt"), "content");
    //
    //     var result = _tools.DeleteDirectory(dirPath);
    //
    //     Assert.Equal($"Directory deleted: {dirPath}", result);
    //     Assert.False(Directory.Exists(dirPath));
    // }
    //
    // [Fact]
    // public void DeleteDirectory_目录不存在_返回错误()
    // {
    //     var dirPath = Path.Combine(_workspaceDir, "nonexistent-dir");
    //
    //     var result = _tools.DeleteDirectory(dirPath);
    //
    //     Assert.Contains("Directory not found", result);
    // }
    //
    // [Fact]
    // public void DeleteDirectory_不允许的目录_拒绝()
    // {
    //     // 尝试删除用户主目录下的路径
    //     var dirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "should-not-delete");
    //
    //     var result = _tools.DeleteDirectory(dirPath);
    //
    //     Assert.Contains("不允许", result);
    // }
    //
    // #endregion

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

        var result = _tools.Write(filePath, "bad content");

        Assert.Contains("不允许", result);
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

        var result = _tools.Edit(filePath, "old", "new");

        Assert.Contains("不允许", result);
    }

    #endregion

    // #region 边界安全测试 — 确保不会误删工作空间/临时目录本身的父目录
    //
    // [Fact]
    // public void DeleteDirectory_不能删除工作空间根目录本身()
    // {
    //     var result = _tools.DeleteDirectory(_workspaceDir);
    //
    //     Assert.Contains("不允许", result);
    //     Assert.True(Directory.Exists(_workspaceDir));
    // }
    //
    // [Fact]
    // public void DeleteDirectory_不能删除临时目录根本身()
    // {
    //     var result = _tools.DeleteDirectory(Path.GetTempPath());
    //
    //     Assert.Contains("不允许", result);
    // }
    //
    // [Fact]
    // public void DeleteFile_不能删除工作空间根目录()
    // {
    //     // DeleteFile 传入目录路径应被拒绝
    //     var result = _tools.DeleteFile(_workspaceDir);
    //
    //     Assert.Contains("不允许", result);
    // }
    //
    // [Fact]
    // public void DeleteFile_路径遍历攻击_拒绝()
    // {
    //     // 尝试通过 ../ 逃逸到 workspace 外
    //     var attackPath = Path.Combine(_workspaceDir, "..", "..", "etc", "passwd");
    //
    //     var result = _tools.DeleteFile(attackPath);
    //
    //     Assert.Contains("不允许", result);
    // }
    //
    // [Fact]
    // public void DeleteDirectory_路径遍历攻击_拒绝()
    // {
    //     var attackPath = Path.Combine(_workspaceDir, "..", "..", "Windows");
    //
    //     var result = _tools.DeleteDirectory(attackPath);
    //
    //     Assert.Contains("不允许", result);
    // }
    //
    // #endregion
}
