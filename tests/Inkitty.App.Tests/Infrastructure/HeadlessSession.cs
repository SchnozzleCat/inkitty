using Avalonia.Headless;

namespace Inkitty.App.Tests.Infrastructure;

/// <summary>
/// One Avalonia headless session for the whole test run. Avalonia only ships
/// xUnit/NUnit integrations, so TUnit tests dispatch onto this session explicitly.
/// </summary>
public static class HeadlessSession
{
    private static HeadlessUnitTestSession? s_session;

    public static HeadlessUnitTestSession Instance =>
        s_session ?? throw new InvalidOperationException("Headless session has not been started.");

    [Before(TestSession)]
    public static void Start() => s_session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    [After(TestSession)]
    public static void Stop()
    {
        s_session?.Dispose();
        s_session = null;
    }

    public static Task Run(Action action) => Instance.Dispatch(action, CancellationToken.None);

    public static Task<T> Run<T>(Func<T> action) => Instance.Dispatch(action, CancellationToken.None);

    /// <summary>
    /// Runs an async test body on the UI thread and awaits it. (Passing a <c>Func&lt;Task&gt;</c> to
    /// <c>Dispatch(Action)</c> would silently fire-and-forget, so route through the <c>Task&lt;T&gt;</c> overload.)
    /// </summary>
    public static Task Run(Func<Task> action) =>
        Instance.Dispatch<bool>(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None);
}
