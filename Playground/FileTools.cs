using ManInBlack.AI.Attributes;

namespace Playground;

[ServiceRegister.Scoped]
public partial class FileTools
{
    [AiTool]
    public string ReadFile(string fileName)
    {
        var file = File.ReadAllText(fileName);
        return file;
    }

    [AiTool]
    public string CreateFile(string fileName)
    {
        File.Create(fileName);
        return $"File {fileName} created.";
    }
    
    [AiTool]
    public string WriteFile(string fileName, string content)
    {
        var file = File.CreateText(fileName);
        file.Write(content);
        return $"File {fileName} written.";
    }
}