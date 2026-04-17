namespace FeishuAdaptor.Helper;

public static class ThrowHelper
{
	public static void ThrowIfNull<T>(this T? obj, string? paramName = null)
	{
		if (obj is null)
		{
			throw new ArgumentNullException(paramName);
		}
	}
	
	public static void ThrowIfNullOrEmpty(this string? str, string? paramName = null)
	{
		if (string.IsNullOrEmpty(str))
		{
			throw new ArgumentNullException(paramName);
		}
	}
}