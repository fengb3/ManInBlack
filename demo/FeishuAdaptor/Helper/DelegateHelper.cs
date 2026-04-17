namespace FeishuAdaptor.Helper;

public static class DelegateHelper
{
	public static Delegate? Compose(params Delegate[] delegates)
	{
		return delegates.Aggregate<Delegate?, Delegate?>(null, Delegate.Combine);
	}
	
	// public static Delegate Curry(Delegate @delegate, object[] args)
	// {
	// 	return Delegate.CreateDelegate(@delegate.GetType(), @delegate.Target, @delegate.Method, args);
	// }
	
	// public static Delegate WarpWithTryCatch(Delegate @delegate)
	// {
	// 	return Delegate.CreateDelegate(@delegate.GetType(), (Action) (() =>
	// 	{
	// 		try
	// 		{
	// 			@delegate.DynamicInvoke();
	// 		}
	// 		catch (Exception e)
	// 		{
	// 			Console.WriteLine(e);
	// 		}
	// 	}));
	// }
}