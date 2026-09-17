namespace VsixTreeViewer.Test;

[TestClass]
public sealed class VsixRefreshCoordinatorTests
{
    [TestMethod]
    public void SuccessfulBuildSchedulesForcedRefresh()
    {
        var dispatcher = new TestDebounceDispatcher();
        bool? force = null;
        using var coordinator = new VsixRefreshCoordinator("project", value => force = value, dispatcher);

        coordinator.BuildStarted();
        coordinator.BuildCompleted(success: true);
        dispatcher.RunPending();

        Assert.AreEqual(1, dispatcher.CancelCount);
        Assert.IsTrue(force);
    }

    [TestMethod]
    public void FailedBuildDoesNotScheduleRefresh()
    {
        var dispatcher = new TestDebounceDispatcher();
        using var coordinator = new VsixRefreshCoordinator("project", _ => Assert.Fail(), dispatcher);

        coordinator.BuildStarted();
        coordinator.BuildCompleted(success: false);

        Assert.IsFalse(dispatcher.HasPendingAction);
    }

    [TestMethod]
    public void BuildStartCancelsPendingWatcherRefresh()
    {
        var dispatcher = new TestDebounceDispatcher();
        using var coordinator = new VsixRefreshCoordinator("project", _ => Assert.Fail(), dispatcher);

        coordinator.Schedule(force: false);
        coordinator.BuildStarted();
        dispatcher.RunPending();

        Assert.AreEqual(1, dispatcher.CancelCount);
    }

    [TestMethod]
    public void WatcherEventsAreIgnoredWhileBuildIsRunning()
    {
        var dispatcher = new TestDebounceDispatcher();
        using var coordinator = new VsixRefreshCoordinator("project", _ => Assert.Fail(), dispatcher);

        coordinator.BuildStarted();
        coordinator.Schedule(force: false);

        Assert.IsFalse(dispatcher.HasPendingAction);
    }

    [TestMethod]
    public void LatestRefreshRequestWins()
    {
        var dispatcher = new TestDebounceDispatcher();
        bool? force = null;
        using var coordinator = new VsixRefreshCoordinator("project", value => force = value, dispatcher);

        coordinator.Schedule(force: false);
        coordinator.Schedule(force: true);
        dispatcher.RunPending();

        Assert.IsTrue(force);
        Assert.AreEqual(2, dispatcher.DebounceCount);
    }

    [TestMethod]
    public void DisposedCoordinatorCancelsAndRejectsCallbacks()
    {
        var dispatcher = new TestDebounceDispatcher();
        var coordinator = new VsixRefreshCoordinator("project", _ => Assert.Fail(), dispatcher);

        coordinator.Schedule(force: true);
        coordinator.Dispose();
        dispatcher.RunPending();
        coordinator.Schedule(force: true);
        coordinator.BuildStarted();
        coordinator.BuildCompleted(success: true);

        Assert.AreEqual(1, dispatcher.CancelCount);
        Assert.IsFalse(dispatcher.HasPendingAction);
    }

    private sealed class TestDebounceDispatcher : IDebounceDispatcher
    {
        private Action? _action;

        public int CancelCount { get; private set; }
        public int DebounceCount { get; private set; }
        public bool HasPendingAction => _action != null;

        public void Debounce(string key, Action action, int milliseconds)
        {
            DebounceCount++;
            _action = action;
        }

        public void Cancel(string key)
        {
            CancelCount++;
            _action = null;
        }

        public void RunPending()
        {
            Action? action = _action;
            _action = null;
            action?.Invoke();
        }
    }
}
