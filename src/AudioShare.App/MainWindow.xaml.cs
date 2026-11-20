using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Interop;
using AudioShare.App.ViewModels;

namespace AudioShare.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _tray;
    private bool _exiting;
    private bool _hintShown;
    private readonly Hotkeys _hotkeys;

    /// <summary>截图用：只显示界面，不碰音频、托盘和快捷键。</summary>
    internal MainWindow(object previewContext)
    {
        InitializeComponent();
        DataContext = previewContext;
        _viewModel = null!;
        _tray = null!;
        _hotkeys = null!;
        _exiting = true;
    }

    internal void ShowSettingsPage(bool show) => SettingsPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _tray = new TrayIcon(_viewModel, ShowFromTray, ExitApp);
        WindowTheme.Apply(this);

        // 开机自启时窗口不显示，也要先创建窗口句柄，全局快捷键才能收到
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hotkeys = new Hotkeys(hwnd);
        _hotkeys.TogglePressed += () =>
        {
            _viewModel.Toggle();
            _tray.ShowHint(_viewModel.IsRunning ? "已开始共享" : "已停止共享");
        };
        _hotkeys.PresetPressed += index => _viewModel.SelectPresetByIndex(index);
        _viewModel.StateNotice += message => _tray.ShowHint(message);
        _viewModel.Update.Found += update => _tray.ShowHint($"AudioShare 有新版本 {update.Version.ToString(3)}，打开窗口可以一键更新。");
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.HotkeysEnabled)) ApplyHotkeys();
        };
        ApplyHotkeys();
    }

    private void ApplyHotkeys()
    {
        if (!_viewModel.HotkeysEnabled)
        {
            _hotkeys.Unregister();
            return;
        }
        var failed = _hotkeys.Register();
        if (failed.Count > 0)
            _viewModel.ShowStatus($"快捷键 {string.Join("、", failed)} 被其他程序占用了，这几个快捷键不可用");
    }

    /// <summary>自动更新后重启：上次在共享就接着共享。</summary>
    public void ResumeAfterUpdate()
    {
        _viewModel.ResumeIfWasRunning();
        _viewModel.ShowStatus($"已更新到 {Updater.CurrentVersion.ToString(3)}");
    }

    /// <summary>开机自启：不弹窗口，只在托盘里，上次在共享就接着共享。</summary>
    public void StartInTray()
    {
        _viewModel.ResumeIfWasRunning();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _hotkeys.Dispose();
        _viewModel.Dispose();
        _tray.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    public void EmergencyRestore()
    {
        try { _viewModel.Dispose(); } catch { }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // 关窗口只是藏到托盘，声音继续
            e.Cancel = true;
            Hide();
            if (!_hintShown)
            {
                _hintShown = true;
                _tray.ShowHint(_viewModel.IsRunning
                    ? "AudioShare 还在后台共享声音。右键托盘图标可以停止或退出。"
                    : "AudioShare 已缩小到托盘。右键托盘图标可以退出。");
            }
        }
        base.OnClosing(e);
    }

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        var exe = await _viewModel.Update.InstallAsync();
        if (exe == null) return;
        // 先正常退出（恢复系统设置、记住正在共享），再启动新版本
        ExitApp();
        Updater.Restart(exe);
    }

    private void OnResetEffects(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DeviceViewModel device) device.ResetEffects();
    }

    private void OnSkipUpdate(object sender, RoutedEventArgs e) => _viewModel.Update.Skip();

    private void OnUpdateDetails(object sender, RoutedEventArgs e) =>
        Updater.OpenReleasesPage(_viewModel.Update.Available?.PageUrl);

    private async void OnCheckUpdate(object sender, RoutedEventArgs e) => await _viewModel.Update.CheckAsync(manual: true);

    private void OnCalibrate(object sender, RoutedEventArgs e)
    {
        new CalibrationWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "保存方案", "给当前的设备设置起个名字，例如「蓝牙 + 有线看电影」",
            _viewModel.SelectedPreset ?? _viewModel.SuggestPresetName());
        if (!string.IsNullOrEmpty(name)) _viewModel.SavePreset(name);
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        if (MessageBox.Show(this, $"删除方案「{name}」？", "删除方案", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            _viewModel.DeletePreset(name);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        _viewModel.EditingDevice = null;
        SettingsPage.Visibility = Visibility.Visible;
    }

    private void OnCloseSettings(object sender, RoutedEventArgs e) => SettingsPage.Visibility = Visibility.Collapsed;

    private void OnEditDevice(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DeviceViewModel device) _viewModel.EditingDevice = device;
    }

    private void OnCloseEditor(object sender, RoutedEventArgs e) => _viewModel.EditingDevice = null;

    /// <summary>设备卡片上调音量的那一行：点空白处不要误把设备关掉。</summary>
    private void OnSwallowClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// <summary>方案一行放不下时，鼠标滚轮横向滚动。</summary>
    private void OnPresetWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_viewModel.EditingDevice != null) _viewModel.EditingDevice = null;
            else if (SettingsPage.Visibility == Visibility.Visible) SettingsPage.Visibility = Visibility.Collapsed;
            else return;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
