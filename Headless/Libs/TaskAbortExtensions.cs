namespace Headless.Libs;

public static class TaskAbortExtensions
{
    /// <summary>
    /// <paramref name="task"/> の完了を待つ。先に <paramref name="abortSignal"/> が完了した場合は待つのをやめ、
    /// <paramref name="createException"/> が返す例外を投げる (<paramref name="task"/> はキャンセルされず放置される)
    /// </summary>
    public static async Task<T> WaitOrAbortAsync<T>(this Task<T> task, Task abortSignal, Func<Exception> createException)
    {
        if (await Task.WhenAny(task, abortSignal) != task)
        {
            throw createException();
        }
        return await task;
    }
}
