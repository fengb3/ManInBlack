namespace FeishuAdaptor.Helper;

public static class StreamHelper
{
    public static async Task<Stream> CopyToMemoryStreamAsync(this Stream source)
    {
        var memoryStream = new MemoryStream();
        await source.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }
}