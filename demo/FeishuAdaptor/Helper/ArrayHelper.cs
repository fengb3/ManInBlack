namespace FeishuAdaptor.Helper;

public static class ArrayHelper
{
    public static T PickRandomOne<T>(this T[] array)
    {
        return array[Random.Shared.Next(0, array.Length)];
    }
    
    public static void EnsureLength<T>(ref T[] arr, int length = 8)
    {
        var len = arr.Length;
        
        if(len >= length)
            return;
        
        while(len < length)
        {
            len *= 2;
        }
        
        Array.Resize(ref arr, len);
    }

    public static void Shuffle<T>(this T[] array)
    {
        Random rng = new Random(114514);
        int    n   = array.Length;
        while (n > 1)
        {
            int k = rng.Next(n--);
            (array[n], array[k]) = (array[k], array[n]);
        }
    }

    public static bool IsEmpty<T>(this T[] array)
    {
        return array.Length == 0;
    }

    public static int FindIndexOf<T>(this T[] array, T value)
    {
        return Array.IndexOf(array, value);
    }
}