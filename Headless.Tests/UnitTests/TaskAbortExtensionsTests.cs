using Headless.Libs;

namespace Headless.Tests.UnitTests;

public class TaskAbortExtensionsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task WaitOrAbortAsync_TaskCompletesFirst_ReturnsResult()
    {
        var abort = new TaskCompletionSource();

        var result = await Task.FromResult(42).WaitOrAbortAsync(abort.Task, () => new WorldDestroyedException("w"));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WaitOrAbortAsync_AbortedWhileWaiting_ThrowsWithoutWaitingForTask()
    {
        var neverCompletes = new TaskCompletionSource<int>();
        var abort = new TaskCompletionSource();

        var waiting = neverCompletes.Task.WaitOrAbortAsync(abort.Task, () => new WorldDestroyedException("w"));
        Assert.False(waiting.IsCompleted);

        abort.SetResult();

        await Assert.ThrowsAsync<WorldDestroyedException>(() => waiting.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task WaitOrAbortAsync_AlreadyAborted_Throws()
    {
        var neverCompletes = new TaskCompletionSource<int>();

        await Assert.ThrowsAsync<WorldDestroyedException>(() =>
            neverCompletes.Task.WaitOrAbortAsync(Task.CompletedTask, () => new WorldDestroyedException("w")).WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task WaitOrAbortAsync_TaskFaults_PropagatesOriginalException()
    {
        var abort = new TaskCompletionSource();
        var failing = Task.FromException<int>(new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.WaitOrAbortAsync(abort.Task, () => new WorldDestroyedException("w")));
        Assert.Equal("boom", ex.Message);
    }
}
