using System.Text.Json;

namespace FeishuAdaptor.Helper;

public static class ObjectHelper
{
    public static T? Clone<T>(this T source)
    {
        if (ReferenceEquals(source, null))
        {
            return default;
        }

        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json);
    }
}