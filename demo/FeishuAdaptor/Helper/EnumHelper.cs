using System.ComponentModel;

namespace FeishuAdaptor.Helper;

public static class EnumExtensions
{
	public static string ToCustomString(this Enum value)
	{
		var fieldInfo = value.GetType().GetField(value.ToString());

		if (fieldInfo == null)
		{
			return value.ToString();
		}
		
		var descriptionAttributes = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false) as DescriptionAttribute[];

		if (descriptionAttributes is { Length: > 0 })
		{
			return descriptionAttributes[0].Description;
		}
		else
		{
			return value.ToString();
		}
	}
	
	public static bool TryParseEnum<T>(this string val, out T? result) where T : Enum
	{
		foreach (var field in typeof(T).GetFields())
		{
			var attribute = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;

			if (attribute == null) continue;

			if (attribute.Description != val) continue;
			
			result = (T)field.GetValue(null)!;
			return true;
		}

		result = default;
		return false;
	}
	
	public static T? ToEnum<T>(this string value) where T : Enum
	{
		foreach (var field in typeof(T).GetFields())
		{
			var attribute = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;

			if (attribute == null) continue;

			if (attribute.Description != value) continue;
			
			return (T)field.GetValue(null)!;

		}

		return default;
	}
}
