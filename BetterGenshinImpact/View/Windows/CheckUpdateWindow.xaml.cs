using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Windows.System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.View.Windows;

[ObservableObject]
public partial class CheckUpdateWindow : FluentWindow
{
    public Func<object, CheckUpdateWindowButton, Task>? UserInteraction = null!;

    [ObservableProperty] private bool showUpdateStatus = false;

    [ObservableProperty] private string updateStatusMessage = string.Empty;

    [ObservableProperty] private bool showOtherUpdateTip = false;

    /// <summary>
    /// 更新来源卡片上展示的本分支更新说明。
    /// </summary>
    public string GitSourceDescription => "【本分支】从 GitHub Releases 下载并更新";

    private UpdateOption _option;

    public CheckUpdateWindow(UpdateOption option)
    {
        _option = option ?? throw new ArgumentNullException(nameof(option));
        DataContext = this;
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);

        if (option.Trigger == UpdateTrigger.Manual)
        {
            IgnoreButton.Visibility = Visibility.Collapsed;
        }

        if (option.Channel == UpdateChannel.Alpha)
        {
            WebpagePanel.Height = 0;
            WebpagePanel.Visibility = Visibility.Collapsed;
            UpdateStatusMessageGrid.Height = 0;
            ShowUpdateStatus = false;

            SizeToContent = SizeToContent.Height; // 设置高度为自动
            UpdateLayout();
        }


        Closing += OnClosing;

        // 延迟显示气泡提示
        if (option.Channel != UpdateChannel.Alpha)
        {
            var showTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(0.8)
            };
            showTimer.Tick += (s, e) =>
            {
                showTimer.Stop();
                ShowOtherUpdateTip = true;

                // 5秒后自动消失
                var hideTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(6)
                };
                hideTimer.Tick += (s2, e2) =>
                {
                    hideTimer.Stop();
                    ShowOtherUpdateTip = false;
                };
                hideTimer.Start();
            };
            showTimer.Start();
        }
    }

    protected void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ShowUpdateStatus)
        {
            e.Cancel = true;
        }
    }

    public void NavigateToHtml(string html)
    {
        WebpagePanel?.NavigateToHtml(html);
    }

    [RelayCommand]
    private async Task BackgroundUpdateAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.BackgroundUpdate);
        }
    }

    [RelayCommand]
    private async Task OtherUpdateAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.OtherUpdate);
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.Update);
        }
    }

    /// <summary>
    /// 确认来源后，调用更新器从本分支的 GitHub Releases 检查并安装更新。
    /// </summary>
    [RelayCommand]
    private async Task UpdateFromGitHostPlatformAsync()
    {
        var result = await ThemedMessageBox.ShowAsync(
            "本次更新将从 jiurag/BetterGI_Fix 的 GitHub Releases 下载。\n请确认当前网络可以正常访问 GitHub。\n是否继续？",
            "确认更新源",
            MessageBoxButton.OKCancel,
            ThemedMessageBox.MessageBoxIcon.Warning);
        if (result != MessageBoxResult.OK)
        {
            return;
        }

        await RunUpdaterAsync("-I --source github");
    }

    /// <summary>
    ///  --source github
    /// </summary>
    private async Task RunUpdaterAsync(string parameters)
    {
        // 唤起更新程序
        string updaterExePath = Global.Absolute("BetterGI.update.exe");
        if (!File.Exists(updaterExePath))
        {
            await ThemedMessageBox.ErrorAsync("更新程序不存在，请选择其他更新方式！");
            return;
        }

        // 启动
        Process.Start(updaterExePath, parameters);

        // 退出程序
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private async Task IgnoreAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.Ignore);
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (UserInteraction != null)
        {
            await UserInteraction.Invoke(this, CheckUpdateWindowButton.Cancel);
        }
    }

    private void OnCloseTipClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ShowOtherUpdateTip = false;
    }

    public enum CheckUpdateWindowButton
    {
        BackgroundUpdate,
        OtherUpdate,
        Update,
        Ignore,
        Cancel,
    }
}
