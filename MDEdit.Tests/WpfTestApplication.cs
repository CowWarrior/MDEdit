using System.Windows;

namespace MDEdit.Tests;

// Shared WPF test infrastructure. Application.Current is process-wide state, not per-thread — with
// multiple test classes each spinning up their own STA thread and needing an Application instance
// (any test loading pack:// resources needs one to exist), an unguarded
// "if (Application.Current is null) new Application()" in each is a genuine check-then-create race
// under xUnit's default parallel-by-class execution: two threads can both observe null before either
// finishes constructing. EnsureApplicationCreated is the single, lock-guarded place that creates it.
// RunOnSta is the standard "run this on a dedicated STA thread and rethrow on the caller" wrapper
// every WPF-object-touching test in this project needs (WPF types are apartment-threaded; xUnit's
// own test threads are not guaranteed STA) — was duplicated identically across three test classes
// before being pulled out here.
internal static class WpfTestApplication
{
    private static readonly object Lock = new();

    public static Application EnsureApplicationCreated()
    {
        lock (Lock)
        {
            return Application.Current ?? new Application();
        }
    }

    public static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) throw ex;
    }
}
