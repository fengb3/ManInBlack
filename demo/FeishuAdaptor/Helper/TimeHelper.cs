namespace FeishuAdaptor.Helper;

public static class TimeHelper
{
	public static long ToUnixTimestamp(this DateTime dateTime)
	{
		return ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
	}
	
	public static long ToUnixTimestampMilli(this DateTime dateTime)
	{
		return ((DateTimeOffset)dateTime).ToUnixTimeMilliseconds();
	}
	
	public static DateTime ToDateTime(this long timeStamp)
	{
		return DateTimeOffset.FromUnixTimeMilliseconds(timeStamp).DateTime;	
	}
	
	public static bool IsBetween(this DateTime dateTime, DateTime? start, DateTime? end)
	{
		return dateTime >= start && dateTime <= end;
	}
	
	
	#region Stinky Time
	
	public const long StinkyTime1 = 38050752000; // 1145-01-04
	public const long StinkyTime2 = 24208320000; // 1919-08-10
	public const long StinkyTime3 = 0; // 1970-01-01
	
	#endregion
}