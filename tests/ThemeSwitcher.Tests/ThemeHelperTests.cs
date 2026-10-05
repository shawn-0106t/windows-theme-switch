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

    private readonly ThemeHelper _helper = new(SandboxPersonalize, SandboxRun, broadcast: false);

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
