namespace FlameshotClipboardHelper;

internal sealed class ScreenshotWatcher : IDisposable
{
    private readonly Control _ui;
    private readonly Action<string, bool> _onPushed;
    private FileSystemWatcher? _watcher;
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Windows.Forms.Timer> _debounce = new(StringComparer.OrdinalIgnoreCase);

    public ScreenshotWatcher(Control ui, Action<string, bool> onPushed)
    {
        _ui = ui;
        _onPushed = onPushed;
    }

    public void Start(string watchFolder)
    {
        Stop();

        Directory.CreateDirectory(watchFolder);

        _watcher = new FileSystemWatcher(watchFolder, "*.png")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
    }

    public void Stop()
    {
        foreach (var timer in _debounce.Values)
        {
            timer.Stop();
            timer.Dispose();
        }

        _debounce.Clear();

        if (_watcher is null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileEvent;
        _watcher.Changed -= OnFileEvent;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
            return;

        var path = e.FullPath;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return;

        _ui.BeginInvoke(() => SchedulePush(path));
    }

    private void SchedulePush(string path)
    {
        if (_debounce.TryGetValue(path, out var existing))
        {
            existing.Stop();
            existing.Start();
            return;
        }

        var timer = new System.Windows.Forms.Timer { Interval = 350 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            _debounce.Remove(path);

            if (ShouldSkip(path))
                return;

            var ok = ClipboardHelper.TryPushScreenshot(path);
            _onPushed(path, ok);
        };

        _debounce[path] = timer;
        timer.Start();
    }

    private bool ShouldSkip(string path)
    {
        var now = DateTime.UtcNow;
        foreach (var key in _recent.Keys.Where(k => (now - _recent[k]).TotalSeconds > 5).ToList())
            _recent.Remove(key);

        if (_recent.TryGetValue(path, out var last) && (now - last).TotalSeconds < 1)
            return true;

        _recent[path] = now;
        return false;
    }

    public void Dispose() => Stop();
}
