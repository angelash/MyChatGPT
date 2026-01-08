using System.Collections.Concurrent;
using System.Text;

namespace AudioBridge.Core.Logging;

/// <summary>
/// 简易文件日志系统
/// </summary>
public sealed class FileLogger : IDisposable
{
    private static FileLogger? _instance;
    private static readonly object _lock = new();

    private readonly string _logDir;
    private readonly string _logFile;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly Thread _writeThread;
    private readonly AutoResetEvent _signal = new(false);
    private volatile bool _running = true;

    /// <summary>
    /// 日志事件（用于 UI 显示）
    /// </summary>
    public event Action<string>? LogWritten;

    /// <summary>
    /// 获取单例实例
    /// </summary>
    public static FileLogger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new FileLogger();
                }
            }
            return _instance;
        }
    }

    private FileLogger()
    {
        // 日志目录：exe 所在目录的 logs 子目录
        _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);

        // 日志文件名：audiobridge-YYYY-MM-DD.log
        _logFile = Path.Combine(_logDir, $"audiobridge-{DateTime.Now:yyyy-MM-dd}.log");

        // 启动后台写入线程
        _writeThread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "FileLogger"
        };
        _writeThread.Start();

        Log("INFO", "FileLogger", $"日志系统启动，文件：{_logFile}");
    }

    public void Log(string level, string source, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] [{source}] {message}";
        _queue.Enqueue(line);
        _signal.Set();
        LogWritten?.Invoke(line);
    }

    public void Info(string source, string message) => Log("INFO", source, message);
    public void Warn(string source, string message) => Log("WARN", source, message);
    public void Error(string source, string message) => Log("ERROR", source, message);
    public void Debug(string source, string message) => Log("DEBUG", source, message);

    private void WriteLoop()
    {
        var sb = new StringBuilder();

        while (_running)
        {
            _signal.WaitOne(1000); // 最多等待 1 秒

            sb.Clear();
            while (_queue.TryDequeue(out var line))
            {
                sb.AppendLine(line);
            }

            if (sb.Length > 0)
            {
                try
                {
                    File.AppendAllText(_logFile, sb.ToString());
                }
                catch
                {
                    // 忽略写入失败
                }
            }
        }

        // 退出前刷新剩余日志
        sb.Clear();
        while (_queue.TryDequeue(out var line))
        {
            sb.AppendLine(line);
        }
        if (sb.Length > 0)
        {
            try
            {
                File.AppendAllText(_logFile, sb.ToString());
            }
            catch
            {
                // 忽略
            }
        }
    }

    public string GetLogFilePath() => _logFile;

    public void Dispose()
    {
        _running = false;
        _signal.Set();
        _writeThread.Join(2000);
        _signal.Dispose();
    }
}
