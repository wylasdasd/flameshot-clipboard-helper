namespace FlameshotClipboardHelper.Core;

internal sealed class ScreenshotWatcher : IDisposable
{
    private readonly Action<Action> _marshalToUi;
    private readonly Action<string> _onDetected;
    private FileSystemWatcher? _watcher;
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Threading.Timer> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _debounceGate = new();

    public ScreenshotWatcher(Action<Action> marshalToUi, Action<string> onDetected)
    {
        _marshalToUi = marshalToUi;
        _onDetected = onDetected;
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
        lock (_debounceGate)
        {
            foreach (var timer in _debounce.Values)
                timer.Dispose();
            _debounce.Clear();
        }

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

        _marshalToUi(() => SchedulePush(path));
    }

    private void SchedulePush(string path)
    {
        lock (_debounceGate)
        {
            if (_debounce.TryGetValue(path, out var existing))
            {
                existing.Change(350, Timeout.Infinite);
                return;
            }

            var timer = new System.Threading.Timer(_ =>
            {
                _marshalToUi(() => CompletePush(path));
            }, null, 350, Timeout.Infinite);

            _debounce[path] = timer;
        }
    }

    private void CompletePush(string path)
    {
        lock (_debounceGate)
        {
            if (_debounce.Remove(path, out var timer))
                timer.Dispose();
        }

        if (ShouldSkip(path))
            return;

        _onDetected(path);
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
