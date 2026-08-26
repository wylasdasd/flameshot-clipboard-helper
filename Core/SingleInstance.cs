namespace FlameshotClipboardHelper;

internal static class SingleInstance
{
    private const string MutexName = "FlameshotClipboardHelper_Instance";
    private const string ShowSettingsEventName = "FlameshotClipboardHelper_ShowSettings";

    private static Mutex? _mutex;

    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
            return true;

        try
        {
            using var evt = EventWaitHandle.OpenExisting(ShowSettingsEventName);
            evt.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }

        return false;
    }

    public static EventWaitHandle CreateShowSettingsEvent() =>
        new(false, EventResetMode.AutoReset, ShowSettingsEventName);
}
