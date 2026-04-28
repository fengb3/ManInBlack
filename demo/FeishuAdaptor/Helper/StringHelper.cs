using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FeishuAdaptor.Helper;

public static class StringHelper
{
	static StringHelper()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var unused = Gb2312;
	}
	
	private static readonly JsonSerializerOptions? PrettyOption = new()
	{
		WriteIndented = true,
		Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		// DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private static readonly JsonSerializerOptions? DefaultOption = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		// DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// native aot not work
	/// </summary>
	/// <param name="o"></param>
	/// <param name="isPretty"></param>
	/// <returns></returns>
	public static string ToJsonString(this object? o, bool isPretty = false)
	{
		var options = isPretty switch
		{
			true  => PrettyOption,
			false => DefaultOption
		};

		return JsonSerializer.Serialize(o, options);
	}

	public static string JoinBy<T>(this IEnumerable<T> obj, string separator)
	{
		return string.Join(separator, obj);
	}

	public static string JoinBy<T>(this Span<T> span, string splitter)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < span.Length; i++)
		{
			sb.Append(span[i]);
			if (i != span.Length - 1)
			{
				sb.Append(splitter);
			}
		}

		return sb.ToString();
	}

	public static string Repeats(this string str, int count)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

		var result = new StringBuilder(str.Length * count);
		for (var i = 0; i < count; i++)
		{
			result.Append(str);
		}

		return result.ToString();
	}

	public static string WarpBy(this string s, string warp = "\"")
	{
		return new StringBuilder()
			.Append(warp)
			.Append(s)
			.Append(warp)
			.ToString();
	}

	public static string ToArrayView<T>(this IEnumerable<T> obj)
	{
		return $"[{obj.JoinBy(", ").TrimEnd()}]";
	}

	public static string ToArrayViewTakeStart<T>(this IEnumerable<T> obj, int take)
	{
		return $"[{obj.Take(take).JoinBy(", ").TrimEnd()} ...]";
	}

	#region Cast

	public static bool ToBool(this string s)
	{
		return bool.TryParse(s, out var result) && result;
	}

	public static int ToInt(this string? s)
	{
		return int.TryParse(s, out var result) ? result : 0;
	}

	public static long ToLong(this string? s)
	{
		return long.TryParse(s, out var result) ? result : 0;
	}

	public static double ToDouble(this string s)
	{
		return double.TryParse(s, out var result) ? result : 0;
	}

	public static decimal ToDecimal(this string s)
	{
		return decimal.TryParse(s, out var result) ? result : 0;
	}

	#endregion

	/// <summary>
	/// create a string password should have alphabetic , numeric and special characters
	/// </summary>
	/// <param name="length"></param>
	/// <returns></returns>
	public static string CreateStrongPassword(int length = 12)
	{
		// Define the characters that the password can contain
		const string lowercase    = "abcdefghijklmnopqrstuvwxyz";
		const string uppercase    = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		const string digits       = "1234567890";
		const string specialChars = "!@#$%^&*()-_=+{}[]|:;<>,.?/";

		// Combine all characters into one string
		var allChars = lowercase + uppercase + digits + specialChars;

		// Use a cryptographically secure random number generator
		var password = new char[length];
		var buffer   = new byte[length];

		// Generate the password
		RandomNumberGenerator.Fill(buffer);

		for (var i = 0; i < length; i++)
		{
			// Select a random character from the allChars string
			var index = buffer[i] % allChars.Length;
			password[i] = allChars[index];
		}

		// Ensure the password meets the requirements
		bool hasLower = false, hasUpper = false, hasDigit = false, hasPunctuation = false;

		for (var i = 0; i < length; i++)
		{
			if (char.IsLower(password[i])) hasLower             = true;
			if (char.IsUpper(password[i])) hasUpper             = true;
			if (char.IsDigit(password[i])) hasDigit             = true;
			if (char.IsPunctuation(password[i])) hasPunctuation = true;
		}

		if (!hasLower || !hasUpper || !hasDigit || !hasPunctuation)
		{
			// If the password does not meet the requirements, generate a new one
			return CreateStrongPassword(length);
		}

		return new string(password);
	}

	/// <summary>
	/// 隐藏字符串， 保留前几位和后几位
	/// </summary>
	/// <param name="str"></param>
	/// <param name="remainStart">保留前几个字符 默认4</param>
	/// <param name="remainEnd">保留后几个字符 默认4</param>
	/// <returns></returns>
	public static string MaskString(this string str, int remainStart = 4, int remainEnd = 4)
	{
		const string maskStr = "笑嘻"; // 😂 笑嘻了

		if (string.IsNullOrEmpty(str))
		{
			return str;
		}

		if (str.Length <= remainStart + remainEnd)
		{
			return maskStr.Repeats(str.Length / maskStr.Length) + maskStr[..(str.Length % maskStr.Length)];
		}

		var sb = new StringBuilder(str.Length);
		sb.Append(str.AsSpan(0, remainStart));
		for (var i = 0; i < str.Length - remainStart - remainEnd; i++)
		{
			var maskChar = maskStr[i % maskStr.Length];
			sb.Append(maskChar);
		}

		sb.Append(str.AsSpan(str.Length - remainEnd));

		return sb.ToString();
	}


	#region Chinese

	private static readonly Encoding Gb2312 = Encoding.GetEncoding("GB2312");
	
	/// <summary>
	/// check if the string is chinese
	/// </summary>
	/// <param name="toCheck"></param>
	/// <returns></returns>
	public static bool IsChinese(this string toCheck)
	{
		return Gb2312.GetByteCount(toCheck) == toCheck.Length;
	}

	#endregion
}