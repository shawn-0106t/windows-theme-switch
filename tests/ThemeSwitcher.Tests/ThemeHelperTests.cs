using Microsoft.Win32;
using Xunit;

namespace ThemeSwitcher.Tests;

/// <summary>
/// ThemeHelper 逻辑测试。全部走沙盒注册表键（Software\ThemeSwitcher.Tests），
/// 广播关闭，绝不触碰真实主题设置。
/// </summary>
public sealed class ThemeHelperTests : IDisposable
{
    private const string SandboxRoot = @"Software\ThemeSwitcher.Tests";
    private const string SandboxPersonalize = SandboxRoot + @"\Personalize";
    private const string SandboxRun = SandboxRoot + @"\Run";

    // reapplyDelayMs=0：常规用例跳过延迟重写路径（避免每例多等 120ms），重写路径由专门用例覆盖
    private readonly ThemeHelper _helper = new(SandboxPersonalize, SandboxRun, broadcast: false, reapplyDelayMs: 0);

    public ThemeHelperTests()
    {
        DeleteSandbox();
    }

    public void Dispose()
    {
        DeleteSandbox();
    }

    private static void DeleteSandbox()
    {
        Registry.CurrentUser.DeleteSubKeyTree(SandboxRoot, throwOnMissingSubKey: false);
    }

    private static object? ReadRaw(string path, string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name);
    }

    [Fact]
    public void ReadState_MissingKey_DefaultsToLight()
    {
        ThemeState state = _helper.ReadState();

        Assert.True(state.SystemUsesLightTheme);
        Assert.True(state.AppsUseLightTheme);
    }

    [Fact]
    public void ReadState_NonDwordValue_TreatedAsDefaultLight()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SandboxPersonalize))
        {
            key.SetValue("SystemUsesLightTheme", "not-a-number", RegistryValueKind.String);
            key.SetValue("AppsUseLightTheme", 0, RegistryValueKind.DWord);
        }

        ThemeState state = _helper.ReadState();

        Assert.True(state.SystemUsesLightTheme); // 非法值回落默认浅色
        Assert.False(state.AppsUseLightTheme);   // 合法值正常读取
    }

    [Fact]
    public void SetTheme_WritesBothDwordValues()
    {
        _helper.SetTheme(systemLight: false, appsLight: false);

        Assert.Equal(0, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(0, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));

        _helper.SetTheme(systemLight: true, appsLight: true);

        Assert.Equal(1, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(1, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void SetTheme_NullArgument_LeavesOtherValueUnchanged()
    {
        _helper.SetTheme(systemLight: false, appsLight: false);
        _helper.SetTheme(systemLight: null, appsLight: true);

        Assert.Equal(0, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(1, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void SetTheme_BothNull_NoKeyCreated()
    {
        _helper.SetTheme(systemLight: null, appsLight: null);

        Assert.Null(ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Null(ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void SetTheme_WithReapplyDelay_SynchronousReapplyWritesValues()
    {
        // 覆盖生产配置的延迟重写路径（broadcast 关闭，不干扰桌面）。
        // 断言耗时证明延迟被同步等待（返回前完成），而非 fire-and-forget 被 CLI 进程退出截断。
        var slowHelper = new ThemeHelper(SandboxPersonalize, SandboxRun, broadcast: false, reapplyDelayMs: 30);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        slowHelper.SetTheme(systemLight: false, appsLight: true);

        watch.Stop();
        Assert.True(
            watch.ElapsedMilliseconds >= 30,
            $"reapply delay was not awaited synchronously (elapsed {watch.ElapsedMilliseconds}ms)");
        Assert.Equal(0, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(1, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void SetTheme_ReapplySkipped_WhenRegistryChangedDuringDelay()
    {
        // 延迟窗口内"外部"（其他实例/设置页）写入新值时，重写让位不覆盖：
        // 首次写 system=false/apps=true，延迟期内外部改 apps=false，旧目标值不得被写回
        var racingHelper = new ThemeHelper(SandboxPersonalize, SandboxRun, broadcast: false, reapplyDelayMs: 120);

        bool externalWriteDone = false;
        var writer = new Thread(() =>
        {
            // 等首次写入两个值都落地（避免抢在首次写入前被覆盖），再模拟外部切换。
            // 设上限防未来回归导致首写不落地时 Join 无限挂起
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while ((ReadRaw(SandboxPersonalize, "SystemUsesLightTheme") is not 0
                    || ReadRaw(SandboxPersonalize, "AppsUseLightTheme") is not 1)
                && deadline.ElapsedMilliseconds < 5000)
            {
                Thread.Sleep(2);
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(SandboxPersonalize, writable: true);
            key.SetValue("AppsUseLightTheme", 0, RegistryValueKind.DWord);
            externalWriteDone = true;
        })
        {
            IsBackground = true,
        };
        writer.Start();

        racingHelper.SetTheme(systemLight: false, appsLight: true);

        writer.Join();
        Assert.True(externalWriteDone);
        Assert.Equal(0, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme")); // 首次写入保留
        Assert.Equal(0, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));    // 外部新值未被重写覆盖
    }

    [Fact]
    public void Constructor_NegativeReapplyDelay_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ThemeHelper(SandboxPersonalize, SandboxRun, broadcast: false, reapplyDelayMs: -1));
    }

    [Fact]
    public void RepairTheme_RewritesCurrentValuesAsDword()
    {
        _helper.SetTheme(systemLight: false, appsLight: true);
        // 把 apps 值改坏为非 DWORD：修复的等值重写必须把它写回 DWord（证明写入确实发生，
        // 而非 no-op 也通过断言）
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SandboxPersonalize))
        {
            key.SetValue("AppsUseLightTheme", "corrupted", RegistryValueKind.String);
        }

        _helper.RepairTheme(refreshSystemParams: false);

        Assert.Equal(0, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(1, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void ToggleAll_FlipsBothValues()
    {
        _helper.SetTheme(systemLight: true, appsLight: true);

        _helper.ToggleAll();

        Assert.Equal(0, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(0, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void ToggleAll_FromMixedState_FlipsBothKeepingMixed()
    {
        _helper.SetTheme(systemLight: false, appsLight: true);

        _helper.ToggleAll();

        Assert.Equal(1, ReadRaw(SandboxPersonalize, "SystemUsesLightTheme"));
        Assert.Equal(0, ReadRaw(SandboxPersonalize, "AppsUseLightTheme"));
    }

    [Fact]
    public void AutoStart_Enabled_WritesStringValueWithMinimizedFlag()
    {
        _helper.SetAutoStart(true);

        object? raw = ReadRaw(SandboxRun, ThemeHelper.AutoStartValueName);
        Assert.IsType<string>(raw);
        Assert.Contains("--minimized", Assert.IsType<string>(raw));
    }

    [Fact]
    public void AutoStart_Disabled_RemovesValue()
    {
        _helper.SetAutoStart(true);
        _helper.SetAutoStart(false);

        Assert.Null(ReadRaw(SandboxRun, ThemeHelper.AutoStartValueName));
        Assert.False(_helper.IsAutoStartEnabled());
    }

    [Fact]
    public void AutoStart_IsEnabled_ReflectsRegistryState()
    {
        Assert.False(_helper.IsAutoStartEnabled());

        _helper.SetAutoStart(true);
        Assert.True(_helper.IsAutoStartEnabled());

        _helper.SetAutoStart(false);
        Assert.False(_helper.IsAutoStartEnabled());
    }

    [Fact]
    public void AutoStart_DisabledWhenMissing_DoesNotThrow()
    {
        _helper.SetAutoStart(false);

        Assert.False(_helper.IsAutoStartEnabled());
    }
}
