namespace FeishuAdaptor.Helper;

public class While
{
	// public static void True(Action action, )
	// {
	// 	while (true)
	// 	{
	// 		action.Invoke();
	// 	}
	// }

	public static void Condition(Func<bool> condition, Action action)
	{
		while (condition())
		{
			action();
		}
	}

	public static void Do(Action action, Func<bool> condition)
	{
		do
		{
			action();
		} while (condition());
	}
}

public static class For
{
	public static void Range(int start, int end, Action<int> action)
	{
		for (var i = start; i < end; i++)
		{
			action(i);
		}
	}

	public static void Times(this int times, Action<int> action)
	{
		Range(0, times, action);
	}
}