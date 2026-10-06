using System.Runtime.InteropServices;

namespace ThemeSwitcher;

internal static class Program
{
    private const string MutexName = @"Local\ThemeSwitcher.SingleInstance";
    private const string ActivateEventName = @"Local\ThemeSwitcher.Activate";
    private const int AttachParentProcess = -1;
    private const int AsfwAny = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && !args[0].Equals("--minimized", StringComparison.OrdinalIgnoreCase))
        {
            return RunCli(args);
        }

        return RunGui(startMinimized: args.Length > 0);
    }

    // ---- GUI 模式（FR-6/FR-7：单实例 + 托盘启动）----

    private static int RunGui(bool startMinimized)
    {
        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            acquired = true; // 上一个实例异常退出遗留的锁，直接接管
        }

        if (!acquired)
        {
            SignalExistingInstance();
            return 0;
        }

        try
        {
            // 事件必须先于窗体创建：否则窗体构造期间启动的第二实例找不到事件，静默退出
            using var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var form = new MainForm(startMinimized);
            StartActivationListener(form, activateEvent);
            Application.Run(form);
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        return 0;
    }

    /// <summary>后台线程等待激活事件：第二实例启动时把已有窗口带到前台。</summary>
    private static void StartActivationListener(MainForm form, EventWaitHandle activateEvent)
    {
        _ = form.Handle; // 提前创建句柄，后台线程才能 BeginInvoke
        var listener = new Thread(() =>
        {
            try
            {
                while (activateEvent.WaitOne())
                {
                    try
                    {
                        form.BeginInvoke(() => form.ShowAndActivate());
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return; // 句柄已释放：应用正在退出
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // 事件在应用退出时被释放，监听结束
            }
        })
        {
            IsBackground = true,
        };
        listener.Start();
    }

    private static void SignalExistingInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ActivateEventName, out EventWaitHandle? existing))
        {
            // 新启动进程可能持有前台权限（用户刚点击了 exe），转移给已有实例使其能真正前置窗口
            _ = AllowSetForegroundWindow(AsfwAny);
            using (existing)
            {
                existing.Set();
            }
        }
    }

    // ---- CLI 模式（FR-10）：无窗口执行后退出，供脚本与端到端验证 ----

    private static int RunCli(IReadOnlyList<string> args)
    {
        TryAttachParentConsole();

        var helper = new ThemeHelper();
        switch (args[0].ToLowerInvariant())
        {
            case "--status":
                PrintState(helper.ReadState());
                return 0;

            case "--toggle":
                helper.ToggleAll();
                PrintState(helper.ReadState());
                return 0;

            case "--set" when args.Count == 2:
                switch (args[1].ToLowerInvariant())
                {
                    case "dark":
                        helper.SetTheme(systemLight: false, appsLight: false);
                        PrintState(helper.ReadState());
                        return 0;
                    case "light":
                        helper.SetTheme(systemLight: true, appsLight: true);
                        PrintState(helper.ReadState());
                        return 0;
                    default:
                        PrintUsage();
                        return 2;
                }

            case "--set-sys" when args.Count == 2 && IsMode(args[1]):
                helper.SetTheme(systemLight: args[1].Equals("light", StringComparison.OrdinalIgnoreCase), appsLight: null);
                PrintState(helper.ReadState());
                return 0;

            case "--set-apps" when args.Count == 2 && IsMode(args[1]):
                helper.SetTheme(systemLight: null, appsLight: args[1].Equals("light", StringComparison.OrdinalIgnoreCase));
                PrintState(helper.ReadState());
                return 0;

            case "--repair":
                helper.RepairTheme();
                Console.WriteLine("Theme repair applied.");
                return 0;

            default:
                PrintUsage();
                return 2;
        }
    }

    private static bool IsMode(string value) =>
        value.Equals("light", StringComparison.OrdinalIgnoreCase)
        || value.Equals("dark", StringComparison.OrdinalIgnoreCase);

    private static void PrintState(ThemeState state)
    {
        Console.WriteLine(
            $"System={(state.SystemUsesLightTheme ? "Light" : "Dark")} Apps={(state.AppsUseLightTheme ? "Light" : "Dark")}");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            """
            Usage:
              ThemeSwitcher.exe               Show the main window
              ThemeSwitcher.exe --minimized   Start minimized to the tray
              ThemeSwitcher.exe --toggle      Toggle Windows + apps theme, then exit
              ThemeSwitcher.exe --set dark    Set both to dark, then exit
              ThemeSwitcher.exe --set light   Set both to light, then exit
              ThemeSwitcher.exe --set-sys dark|light    Set Windows mode only, then exit
              ThemeSwitcher.exe --set-apps dark|light   Set apps mode only, then exit
              ThemeSwitcher.exe --repair      Re-apply current theme to wake a stuck taskbar
              ThemeSwitcher.exe --status      Print current theme state
            """);
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static void TryAttachParentConsole()
    {
        try
        {
            _ = AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
            // WinExe 在非 Windows 环境不会运行到此处，兜底忽略
        }
    }
}
