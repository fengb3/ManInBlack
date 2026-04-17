namespace FeishuAdaptor.Helper;

public static class FileHelper
{
    /// <summary>
    /// get file extension name
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static string GetFileExtensionName(string fileName)
    {
        var length = fileName.Length;
        
        var num = length;

        while (--num >= 0)
        {
            var c = fileName[num];
            
            if (c == '.')
            {
                return fileName.Substring(num, length - num);
            }

            if (c is '\\' or '/' or ':')
            {
                break;
            }
        }

        return string.Empty;
    }
}