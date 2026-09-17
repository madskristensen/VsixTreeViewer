using System.Collections.Concurrent;

namespace VsixTreeViewer.Test;

[TestClass]
public sealed class DebouncerTests
{
    [TestMethod]
    public void ConcurrentNotificationsDoNotThrow()
    {
        string key = Guid.NewGuid().ToString();
        var exceptions = new ConcurrentQueue<Exception>();

        Parallel.For(0, 500, index =>
        {
            try
            {
                Debouncer.Debounce(key, () => { }, 50);

                if (index % 5 == 0)
                {
                    Debouncer.Cancel(key);
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        Debouncer.Cancel(key);

        Assert.IsEmpty(exceptions);
    }

    [TestMethod]
    public void OnlyLatestNotificationRuns()
    {
        string key = Guid.NewGuid().ToString();
        using var completed = new ManualResetEventSlim();
        int executionCount = 0;

        for (int index = 0; index < 50; index++)
        {
            Debouncer.Debounce(key, () =>
            {
                Interlocked.Increment(ref executionCount);
                completed.Set();
            }, 50);
        }

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, executionCount);
    }

    [TestMethod]
    public void CancelPreventsPendingAction()
    {
        string key = Guid.NewGuid().ToString();
        int executionCount = 0;

        Debouncer.Debounce(key, () => Interlocked.Increment(ref executionCount), 100);
        Debouncer.Cancel(key);
        Thread.Sleep(250);

        Assert.AreEqual(0, executionCount);
    }

    [TestMethod]
    public void CanScheduleAgainAfterCancellation()
    {
        string key = Guid.NewGuid().ToString();
        using var completed = new ManualResetEventSlim();

        Debouncer.Debounce(key, () => Assert.Fail(), 100);
        Debouncer.Cancel(key);
        Debouncer.Debounce(key, completed.Set, 25);

        Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
    }
}
