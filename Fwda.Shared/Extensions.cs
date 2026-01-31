namespace Fwda.Shared
{
    public static class Extensions
    {
        extension<T>(T obj) where  T : class
        {
            public T X(Action<T> action)
            {
                action(obj);
                return obj;
            }

            public T X<TR>(Func<T, TR> action)
            {
                action(obj);
                return obj;
            }
        }
    }
}