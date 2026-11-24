using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PcReceiver.Models;
using PcReceiver.Services;

namespace PcReceiver;

public partial class MainWindow : Window
{
    private const int DefaultPort = 28555;
    private const double StickTravelPx = 26.0;
    private const double TriggerBarMaxWidth = 126.0;

    private static readonly SolidColorBrush BrushGreen = FreezeBrush(0x22, 0xC5, 0x5E);
    private static readonly SolidColorBrush BrushAmber = FreezeBrush(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush BrushRed = FreezeBrush(0xEF, 0x44, 0x44);
    private static readonly SolidColorBrush BrushBlue = FreezeBrush(0x3B, 0x82, 0xF6);
    private static readonly SolidColorBrush BrushMutedDot = FreezeBrush(0x6B, 0x72, 0x80);

    private static readonly SolidColorBrush BrushDarkKey = FreezeBrush(0x1B, 0x22, 0x2C);
    private static readonly SolidColorBrush BrushDarkDpad = FreezeBrush(0x22, 0x29, 0x35);
    private static readonly SolidColorBrush BrushDarkFace = FreezeBrush(0x16, 0x1B, 0x22);
    private static readonly SolidColorBrush BrushDarkGuide = FreezeBrush(0x15, 0x1A, 0x22);
    private static readonly SolidColorBrush BrushPuckIdle = FreezeBrush(0x2D, 0x36, 0x44);
    private static readonly SolidColorBrush BrushPuckStrokeIdle = FreezeBrush(0x5A, 0x66, 0x78);
    private static readonly SolidColorBrush BrushDarkText = FreezeBrush(0x05, 0x2E, 0x16);
    private static readonly SolidColorBrush BrushWhite = FreezeBrush(0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush BrushKeyBorderIdle = FreezeBrush(0x30, 0x36, 0x3D);

    private readonly ReceiverEngine _engine = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly List<string> _logLines = [];

    private long _lastHzTimestamp = Stopwatch.GetTimestamp();
    private long _lastHzPacketCount;

    public MainWindow()
    {
        InitializeComponent();

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _uiTimer.Tick += UiTimer_Tick;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateIpPills();

        _engine.LogAdded += Engine_LogAdded;
        _engine.StateChanged += Engine_StateChanged;

        if (int.TryParse(PortTextBox.Text.Trim(), out int port) && port is > 0 and <= 65535)
        {
            _engine.Start(port);
        }
        else
        {
            PortTextBox.Text = DefaultPort.ToString(CultureInfo.InvariantCulture);
            _engine.Start(DefaultPort);
        }

        RefreshConnectionUi();
        _uiTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _uiTimer.Stop();
        _engine.LogAdded -= Engine_LogAdded;
        _engine.StateChanged -= Engine_StateChanged;
        _engine.Dispose();
    }

    private void PopulateIpPills()
    {
        IpPillsPanel.Children.Clear();
        AddIpPill("USB: 127.0.0.1", "127.0.0.1");

        foreach (string ip in ReceiverEngine.GetLocalIPv4Addresses())
        {
            AddIpPill($"Wi-Fi: {ip}", ip);
        }
    }

    private void AddIpPill(string label, string address)
    {
        var button = new Button
        {
            Content = label,
            Tag = address,
            Style = (Style)FindResource("PillButtonStyle"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        button.Click += IpPill_Click;
        IpPillsPanel.Children.Add(button);
    }

    private void IpPill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string ip })
        {
            try
            {
                Clipboard.SetText(ip);
                AppendLogLine($"[{DateTime.Now:HH:mm:ss}] Kopyalandı: {ip}");
            }
            catch (Exception ex)
            {
                AppendLogLine($"[{DateTime.Now:HH:mm:ss}] Pano hatası: {ex.Message}");
            }
        }
    }

    private void Engine_LogAdded(string line)
    {
        Dispatcher.BeginInvoke(() => AppendLogLine(line));
    }

    private void Engine_StateChanged()
    {
        Dispatcher.BeginInvoke(RefreshConnectionUi);
    }

    private void AppendLogLine(string line)
    {
        _logLines.Add(line);
        if (_logLines.Count > 200)
        {
            _logLines.RemoveAt(0);
        }

        LogTextBox.Text = string.Join(Environment.NewLine, _logLines);
        LogTextBox.ScrollToEnd();
    }

    private void RefreshConnectionUi()
    {
        VigemWarningBanner.Visibility = _engine.IsVigemMissing ? Visibility.Visible : Visibility.Collapsed;

        if (_engine.IsRunning)
        {
            StartStopButton.Content = "Durdur";
            StartStopButton.Style = (Style)FindResource("DangerButtonStyle");
            PortTextBox.IsEnabled = false;
        }
        else
        {
            StartStopButton.Content = "Başlat";
            StartStopButton.Style = (Style)FindResource("PrimaryButtonStyle");
            PortTextBox.IsEnabled = true;
        }

        if (_engine.IsVigemMissing)
        {
            StatusDot.Fill = BrushRed;
            StatusBadgeText.Text = "ViGEmBus Eksik";
        }
        else if (_engine.IsClientConnected)
        {
            StatusDot.Fill = BrushGreen;
            StatusBadgeText.Text = "Telefon Bağlı • X360 Aktif";
        }
        else if (_engine.IsRunning)
        {
            StatusDot.Fill = BrushAmber;
            StatusBadgeText.Text = $"Dinleniyor :{_engine.CurrentPort}";
        }
        else
        {
            StatusDot.Fill = BrushMutedDot;
            StatusBadgeText.Text = "Durduruldu";
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        long totalPackets = _engine.TotalPackets;
        long now = Stopwatch.GetTimestamp();
        double elapsedSec = (double)(now - _lastHzTimestamp) / Stopwatch.Frequency;

        if (elapsedSec >= 0.5)
        {
            double hz = (totalPackets - _lastHzPacketCount) / elapsedSec;
            HzText.Text = $"{hz:0} Hz";
            _lastHzPacketCount = totalPackets;
            _lastHzTimestamp = now;
        }

        PacketCountText.Text = totalPackets.ToString("N0", CultureInfo.InvariantCulture);

        GamepadSnapshot snap = _engine.LatestSnapshot;

        double lxNorm = snap.LeftThumbX / 32767.0;
        double lyNorm = snap.LeftThumbY / 32767.0;
        double rxNorm = snap.RightThumbX / 32767.0;
        double ryNorm = snap.RightThumbY / 32767.0;

        LsTransform.X = Math.Clamp(lxNorm, -1.0, 1.0) * StickTravelPx;
        LsTransform.Y = -Math.Clamp(lyNorm, -1.0, 1.0) * StickTravelPx;
        RsTransform.X = Math.Clamp(rxNorm, -1.0, 1.0) * StickTravelPx;
        RsTransform.Y = -Math.Clamp(ryNorm, -1.0, 1.0) * StickTravelPx;

        AxesText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"L: {lxNorm:0.00}, {lyNorm:0.00} | R: {rxNorm:0.00}, {ryNorm:0.00}");

        double ltRatio = snap.LeftTrigger / 255.0;
        double rtRatio = snap.RightTrigger / 255.0;
        LtBarFill.Width = ltRatio * TriggerBarMaxWidth;
        RtBarFill.Width = rtRatio * TriggerBarMaxWidth;
        LtText.Text = $"LT  {(int)Math.Round(ltRatio * 100)}%";
        RtText.Text = $"RT  {(int)Math.Round(rtRatio * 100)}%";

        ushort b = snap.Buttons;
        bool lsPressed = (b & XusbButtons.LS) != 0;
        bool rsPressed = (b & XusbButtons.RS) != 0;

        LsClickRing.Opacity = lsPressed ? 1.0 : 0.0;
        LsPuckCircle.Fill = lsPressed ? BrushGreen : BrushPuckIdle;
        LsPuckCircle.Stroke = lsPressed ? BrushGreen : BrushPuckStrokeIdle;

        RsClickRing.Opacity = rsPressed ? 1.0 : 0.0;
        RsPuckCircle.Fill = rsPressed ? BrushGreen : BrushPuckIdle;
        RsPuckCircle.Stroke = rsPressed ? BrushGreen : BrushPuckStrokeIdle;

        SetShoulderState(LbShape, (b & XusbButtons.LB) != 0);
        SetShoulderState(RbShape, (b & XusbButtons.RB) != 0);

        DpadUpShape.Background = (b & XusbButtons.DpadUp) != 0 ? BrushGreen : BrushDarkDpad;
        DpadDownShape.Background = (b & XusbButtons.DpadDown) != 0 ? BrushGreen : BrushDarkDpad;
        DpadLeftShape.Background = (b & XusbButtons.DpadLeft) != 0 ? BrushGreen : BrushDarkDpad;
        DpadRightShape.Background = (b & XusbButtons.DpadRight) != 0 ? BrushGreen : BrushDarkDpad;

        SetShoulderState(ViewShape, (b & XusbButtons.View) != 0);
        SetShoulderState(MenuShape, (b & XusbButtons.Menu) != 0);

        bool guidePressed = (b & XusbButtons.Guide) != 0;
        GuideShape.Background = guidePressed ? BrushGreen : BrushDarkGuide;
        GuideShape.BorderBrush = guidePressed ? BrushGreen : BrushKeyBorderIdle;
        GuideLetter.Foreground = guidePressed ? BrushDarkText : BrushMutedDot;

        SetFaceButtonState(BtnA, BtnAText, BrushGreen, BrushDarkText, (b & XusbButtons.A) != 0);
        SetFaceButtonState(BtnB, BtnBText, BrushRed, BrushWhite, (b & XusbButtons.B) != 0);
        SetFaceButtonState(BtnX, BtnXText, BrushBlue, BrushWhite, (b & XusbButtons.X) != 0);
        SetFaceButtonState(BtnY, BtnYText, BrushAmber, BrushDarkText, (b & XusbButtons.Y) != 0);

        VisualizerHintText.Text = _engine.IsClientConnected
            ? $"LT {(int)Math.Round(ltRatio * 100)}% • RT {(int)Math.Round(rtRatio * 100)}%"
            : "Telefon bağlantısı bekleniyor...";
    }

    private static void SetShoulderState(Border border, bool pressed)
    {
        border.Background = pressed ? BrushGreen : BrushDarkKey;
        border.BorderBrush = pressed ? BrushGreen : BrushKeyBorderIdle;
    }

    private static void SetFaceButtonState(
        Border border,
        TextBlock label,
        SolidColorBrush accent,
        SolidColorBrush pressedText,
        bool pressed)
    {
        border.Background = pressed ? accent : BrushDarkFace;
        label.Foreground = pressed ? pressedText : accent;
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            return;
        }

        if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port is <= 0 or > 65535)
        {
            AppendLogLine($"[{DateTime.Now:HH:mm:ss}] Geçersiz port numarası (1-65535).");
            return;
        }

        _engine.Start(port);
    }

    private async void AdbButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port is <= 0 or > 65535)
        {
            port = _engine.CurrentPort;
        }

        AdbButton.IsEnabled = false;
        try
        {
            (bool _, string message) = await ReceiverEngine.RunAdbReverseAsync(port);
            AppendLogLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        finally
        {
            AdbButton.IsEnabled = true;
        }
    }

    private void JoyCplButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("joy.cpl") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLogLine($"[{DateTime.Now:HH:mm:ss}] joy.cpl açılamadı: {ex.Message}");
        }
    }

    private void DownloadVigemButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/nefarius/ViGEmBus/releases")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppendLogLine($"[{DateTime.Now:HH:mm:ss}] Tarayıcı açılamadı: {ex.Message}");
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _logLines.Clear();
        LogTextBox.Clear();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static SolidColorBrush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
