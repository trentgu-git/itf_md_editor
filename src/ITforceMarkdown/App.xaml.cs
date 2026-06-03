using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ITforceMarkdown.Services;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown;

/// <summary>
/// Application 入口 + 全局异常黑匣子。
///
/// Windows 上没签名的 self-contained exe 启动出错时不弹任何窗口, 用户
/// 看到的就是"双击没反应"。我们在 *static* constructor 里就挂
/// AppDomain.CurrentDomain.UnhandledException, 比 App.xaml 的解析还要早,
/// 这样几乎所有路径上的崩溃都会被记到 %TEMP%\ITforceMarkdown-startup.log。
///
/// 出现问题时让用户 Win+R → %TEMP% → 找这个日志发给我。
/// </summary>
public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "ITforceMarkdown-startup.log");

    private static WorkspaceStore? _store;
    public static WorkspaceStore Store
        => _store ?? throw new InvalidOperationException("WorkspaceStore not initialized yet.");

    // ─────────────────── 静态 ctor: 比 InitializeComponent 还早 ───────────────────
    static App()
    {
        try
        {
            // 先记一条 "static ctor ran" 进日志, 后面如果连这条都没有
            // 说明根本没进入到这里 (比如 AV 在 image load 之前就拦住了)。
            WriteLine($"--- New session ---");
            WriteLine($"Process: {Process.Path()}");
            WriteLine($".NET: {Environment.Version}");
            WriteLine($"OS: {Environment.OSVersion}");
            WriteLine($"User: {Environment.UserName}");
            WriteLine("static App() ctor reached");
        }
        catch { /* logging must never throw */ }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            LogException("AppDomain.UnhandledException", ex);
            TryShowFatal(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    // ─────────────────── 实例 ctor: 在 InitializeComponent 之前 ───────────────────
    public App()
    {
        try
        {
            WriteLine("App() instance ctor reached");
            DispatcherUnhandledException += (_, e) =>
            {
                LogException("Dispatcher.UnhandledException", e.Exception);
                TryShowFatal(e.Exception);
                // 不 set Handled, 让 process 死掉, 用户重启时去看日志
            };
        }
        catch { }
    }

    // ─────────────────── OnStartup ───────────────────
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            WriteLine("OnStartup begin");

            // 先装基础 Brush 字典 (DynamicResource 链的尾端)
            ThemeManager.InstallBrushes();
            WriteLine("ThemeManager.InstallBrushes ok");

            _store = new WorkspaceStore();
            WriteLine($"WorkspaceStore created. Appearance={_store.Appearance}");

            ThemeManager.Initialize(_store.Appearance);
            WriteLine("ThemeManager.Initialize ok");

            _store.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(WorkspaceStore.Appearance))
                {
                    try { ThemeManager.Apply(_store!.Appearance); }
                    catch (Exception ex) { LogException("ThemeManager.Apply", ex); }
                }
            };

            base.OnStartup(e);
            WriteLine("base.OnStartup ok (MainWindow being shown)");
        }
        catch (Exception ex)
        {
            LogException("OnStartup", ex);
            TryShowFatal(ex);
            Shutdown(1);
        }
    }

    // ─────────────────── logging helpers ───────────────────
    private static void WriteLine(string msg)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n";
            File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
        catch { /* logging must never throw */ }
    }

    private static void LogException(string source, Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ✗ {source}");
            sb.AppendLine($"  Type: {ex.GetType().FullName}");
            sb.AppendLine($"  Message: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            if (ex.InnerException != null)
            {
                sb.AppendLine($"  Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                sb.AppendLine(ex.InnerException.StackTrace);
            }
            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    private static void TryShowFatal(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            MessageBox.Show(
                $"ITforce Markdown failed to start:\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                $"A detailed log has been written to:\n{LogPath}\n\n" +
                "Please send this log file to the developer.",
                "ITforce Markdown — Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 连 MessageBox 都失败了, 没办法, 至少日志写下去了。
        }
    }
}

/// <summary>包一层 Process.GetCurrentProcess().MainModule.FileName, 避免静态 ctor 失败。</summary>
internal static class Process
{
    public static string Path()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "<unknown>"; }
        catch { return "<unknown>"; }
    }
}
