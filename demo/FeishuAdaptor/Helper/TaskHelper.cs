namespace FeishuAdaptor.Helper;

public static  class TaskHelper
{
    public static Task WhenAll(this IEnumerable<Task> tasks)
    {
        return Task.WhenAll(tasks);
    }
    
    public static Task WhenAny(this IEnumerable<Task> tasks)
    {
        return Task.WhenAny(tasks);
    }
    
    
}