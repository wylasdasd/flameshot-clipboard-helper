namespace FlameshotClipboardHelper.Core;

internal static class SingleInstance
{
    private const string MutexName = "FlameshotClipboardHelper_Instance";
    private const string ShowSettingsEventName = "FlameshotClipboardHelper_ShowSettings";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showSettingsEvent;

    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
            return false;

        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        return true;
    }

    public static EventWaitHandle ShowSettingsEvent =>
        _showSettingsEvent ?? throw new InvalidOperationException("ShowSettings event not initialized.");

    /// <summary>Second launch: ask the running instance to open Settings.</summary>
    public static bool TrySignalOpenSettings()
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ShowSettingsEventName);
                evt.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    public static string GetAlreadyRunningMessage()
    {
        var settings = AppSettings.Load();
        Locale.Apply(settings);
        return L.AlreadyRunning;
    }
}
