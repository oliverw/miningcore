namespace Miningcore.Util;

public static class ActionUtils
{
    public static async Task Guard(Func<Task> func, Action<Exception> errorHandler = null)
    {
        try
        {
            await func();
        }

        catch (Exception ex)
        {
            errorHandler?.Invoke(ex);
        }
    }

    public static async Task Guard(Task task, Action<Exception> errorHandler = null)
    {
        try
        {
            await task;
        }

        catch (Exception ex)
        {
            errorHandler?.Invoke(ex);
        }
    }

    public static void Guard(Action func, Action<Exception> errorHandler = null)
    {
        try
        {
            func();
        }

        catch (Exception ex)
        {
            errorHandler?.Invoke(ex);
        }
    }

    public static async Task<T> Guard<T>(Func<Task<T>> func, Action<Exception> errorHandler = null)
    {
        try
        {
            return await func();
        }

        catch (Exception ex)
        {
            errorHandler?.Invoke(ex);

            return default;
        }
    }

    public static T Guard<T>(Func<T> func, Action<Exception> errorHandler = null)
    {
        try
        {
            return func();
        }

        catch (Exception ex)
        {
            errorHandler?.Invoke(ex);

            return default;
        }
    }
}
