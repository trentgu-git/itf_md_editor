using System;
using System.Reflection;
using System.Windows;

namespace ITforceMarkdown;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 启动时显示当前 build 版本 + .NET runtime, 确认 CI 跑过的产物是哪份。
        var asmVersion = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";
        StatusText.Text = $"v{asmVersion}  ·  .NET {Environment.Version}  ·  {Environment.OSVersion}";
    }
}
