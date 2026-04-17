using System.Security.Cryptography;

namespace FeishuAdaptor.Helper;

public static class GUID
{
	public static string GetOne { get; } = Guid.NewGuid().ToString();

	public static string GetShortOne(int length = 8)
	{
		switch (length)
		{
			case < 1:
				throw new ArgumentException("Length must be greater than 0");
			case > 32:
				throw new ArgumentException("Length must be less than 33");
			default:
				return Guid.NewGuid().ToString()[..length];
		}
	}

	public static long GetLong => BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
	
	public static int GetInt => BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
	
	public static int GetSha1Int => BitConverter.ToInt32(SHA1.HashData(Guid.NewGuid().ToByteArray()), 0);
}