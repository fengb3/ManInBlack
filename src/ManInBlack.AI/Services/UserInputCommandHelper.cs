namespace ManInBlack.AI.Services;

public static class UserInputCommandHelper
{ 
    /// <summary>
    /// 处理用户输入 的 command
    /// </summary>
    /// <param name="userInput"></param>
    /// <param name="command"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    public static bool FetchCommand(string userInput, out string? command, out string[]? parameters )
    {
        command = null;
        parameters = null;

        // a command should start with '/',  
        if (userInput.Length <= 1 || !userInput.StartsWith('/')) return false;
        
        var cuts = userInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
        command = cuts[0][1..];
        parameters = cuts.Skip(1).ToArray();
        return true;

    }
}
