using ManInBlack.AI.Core.Attributes;

namespace Playground.Tools;

[ServiceRegister.Scoped]
public partial class FileTools
{
    /// <summary>
    /// read file
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns>file content</returns>
    [AiTool]
    public string ReadFile(string fileName)
    {
        var file = File.ReadAllText(fileName);
        return file;
    }

    /// <summary>
    /// create file
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns>result of file creation</returns>
    [AiTool]
    public string CreateFile(string fileName)
    {
        using (File.Create(fileName)) { }
        return $"File {fileName} created.";
    }
    
    /// <summary>
    /// write file
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="content"></param>
    /// <returns>success if content write to file</returns>
    [AiTool]
    public string WriteFile(string fileName, string content)
    {
        using var file = File.CreateText(fileName);
        file.Write(content);
        return $"File {fileName} written.";
    }
}