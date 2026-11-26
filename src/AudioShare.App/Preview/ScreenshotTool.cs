using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioShare.App.ViewModels;

namespace AudioShare.App.Preview;

/// <summary>
/// 开发用：AudioShare.exe --screenshots 目录
/// 用假数据打开主窗口，把几种典型状态截成 PNG（不访问任何音频设备、不改系统设置）。
/// GitHub Actions 用它在 Windows 上生成界面截图，也顺便检查界面能否正常加载。
/// </summary>
public static class ScreenshotTool
{
    public const string Argument = "--screenshots";

    public static async Task RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var vm = new PreviewViewModel();
        var window = new MainWindow(vm)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();

        await Capture(window, Path.Combine(folder, "1-idle.png"));

        vm.SetRunning(true);
        await Capture(window, Path.Combine(folder, "2-running.png"));

        vm.EditingDevice = vm.Devices[1];
        await Capture(window, Path.Combine(folder, "3-adjust.png"));
        vm.EditingDevice = null;

        window.ShowSettingsPage(true);
        await Capture(window, Path.Combine(folder, "4-settings.png"));
        window.ShowSettingsPage(false);

        // 不同窗口大小下的自适应布局（截图机器的屏幕可能比这还小，所以直接按指定大小排版后截图）
        await Capture(window, Path.Combine(folder, "5-wide.png"), 1320, 780);
        vm.SetRunning(false);
        await Capture(window, Path.Combine(folder, "6-narrow.png"), 520, 900);

        window.Close();
    }

    private static async Task Capture(MainWindow window, string path, double width = 0, double height = 0)
    {
        // 等布局、开关动画走完
        await Task.Delay(600);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        // 只截客户区（不含系统标题栏），截图和窗口里看到的内容一致
        var content = (FrameworkElement)window.Content;
        // 根布局本身是透明的（窗口背景画在窗口上），截图时给它补上背景色，要在排版之前设置才会画出来
        if (content is System.Windows.Controls.Panel panel && panel.Background == null)
            panel.Background = (Brush)window.FindResource("WindowBrush");
        if (width > 0)
        {
            window.ApplyLayout(width);
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
        }
        else
        {
            window.ApplyLayout(content.ActualWidth);
            content.Measure(new Size(content.ActualWidth, content.ActualHeight));
            content.Arrange(new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        double w = width > 0 ? width : content.ActualWidth, h = height > 0 ? height : content.ActualHeight;
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)(w * scale), (int)(h * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(path);
        encoder.Save(file);
    }
}

/// <summary>截图用的假数据，属性名和 MainViewModel 一致。</summary>
public sealed class PreviewViewModel : ObservableObject
{
    private bool _isRunning;
    private DeviceViewModel? _editingDevice;
    private string? _selectedPreset = "蓝牙+有线 看电影";

    public PreviewViewModel()
    {
        void Nop(DeviceViewModel _, string __) { }
        var bt = new DeviceViewModel("1", "WH-1000XM4 蓝牙耳机 (Bluetooth)", "48 kHz · 2 声道", Nop) { IsEnabled = true, Status = "已选中" };
        var wired = new DeviceViewModel("2", "有线耳机 (Realtek USB Audio)", "48 kHz · 2 声道", Nop) { IsEnabled = true, Status = "已选中", Volume = 85, DelayMs = 185, BassDb = 4 };
        var relay = new DeviceViewModel("3", "DELL U2723QE (NVIDIA High Definition Audio)", "48 kHz · 2 声道", Nop) { IsSource = true, Status = "已选为中转设备" };
        var speaker = new DeviceViewModel("4", "扬声器 (Realtek High Definition Audio)", "48 kHz · 2 声道", Nop) { Status = "未启用" };
        Devices = new ObservableCollection<DeviceViewModel> { bt, wired, relay, speaker };
        Sources = new ObservableCollection<SourceOption> { new(null, "自动选择闲置的设备"), new("3", relay.Name) };
        SelectedSource = Sources[0];
        PresetNames = new ObservableCollection<string> { "蓝牙+有线 看电影", "只用音箱", "两人听歌" };
        ToggleCommand = new RelayCommand(() => SetRunning(!IsRunning));
        RefreshCommand = new RelayCommand(() => { });
    }

    public ObservableCollection<DeviceViewModel> Devices { get; }
    public ObservableCollection<SourceOption> Sources { get; }
    public SourceOption? SelectedSource { get; set; }
    public ObservableCollection<string> PresetNames { get; }
    public string? SelectedPreset { get => _selectedPreset; set => Set(ref _selectedPreset, value); }
    public RelayCommand ToggleCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public PreviewUpdate Update { get; } = new();

    public bool IsRunning => _isRunning;
    public bool AlignMode { get; set; } = true;
    public double BaseLatencyMs { get; set; } = 60;
    public bool AutoStartEnabled { get; set; }
    public bool HotkeysEnabled { get; set; } = true;
    public int EnabledCount => Devices.Count(d => d.IsEnabled && !d.IsSource);
    public string SourceBadge => "中转";
    public string SourceTileHint => "系统声音经这里转发，不会出声";
    public string SourceTitle => "中转设备";
    public string SourceHint => "开始后，系统声音会暂时转到这个设备并把它静音，所有打开的设备都播放同一份声音。停止或退出时自动恢复。";

    public string HeroTitle => IsRunning ? $"正在同步播放到 {EnabledCount} 台设备" : "选好设备，点一下开始";

    public string StatusText => IsRunning
        ? "正在把电脑声音同步到 2 个设备"
        : "打开要同时播放的设备，然后点击「开始共享」";

    public DeviceViewModel? EditingDevice { get => _editingDevice; set => Set(ref _editingDevice, value); }

    public void SetRunning(bool running)
    {
        _isRunning = running;
        Devices[0].Status = running ? "同步中 · 缓冲 61 ms" : "已选中";
        Devices[1].Status = running ? "同步中 · 缓冲 246 ms" : "已选中";
        Devices[2].Status = running ? "中转设备 · 已静音，不出声" : "已选为中转设备";
        foreach (var (d, level) in Devices.Zip(new[] { 0.62, 0.55, 0.7, 0.0 }))
            d.Level = running ? level : 0;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(StatusText));
    }
}

public sealed class PreviewUpdate
{
    public bool HasUpdate => false;
    public string BannerText => "";
    public string Message => "已是最新版本";
    public bool IsBusy => false;
    public string CurrentVersionText => $"当前版本 {Updater.CurrentVersion.ToString(3)}";
    public bool CheckUpdates { get; set; } = true;
}
