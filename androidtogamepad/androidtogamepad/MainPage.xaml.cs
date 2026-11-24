#if ANDROID
using androidtogamepad.Platforms.Android.Net;
using androidtogamepad.Platforms.Android.Services;
#endif

namespace androidtogamepad;

public partial class MainPage : ContentPage
{
    private const string HostPrefKey = "pc_host";
    private const string PortPrefKey = "pc_port";
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 28555;

    private bool _isPinned;

    public MainPage()
    {
        InitializeComponent();

        HostEntry.Text = Preferences.Default.Get(HostPrefKey, DefaultHost);
        PortEntry.Text = Preferences.Default.Get(PortPrefKey, DefaultPort).ToString();

#if ANDROID
        StatusLabel.Text = PcLink.Instance.CurrentStatus;
#else
        StatusLabel.Text = "Bu uygulama yalnızca Android cihazda çalışır.";
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        PcLink.Instance.StatusChanged += OnLinkStatusChanged;
        MainActivity.InputDebug += OnInputDebug;
        StatusLabel.Text = PcLink.Instance.CurrentStatus;
#endif
    }

    protected override void OnDisappearing()
    {
#if ANDROID
        PcLink.Instance.StatusChanged -= OnLinkStatusChanged;
        MainActivity.InputDebug -= OnInputDebug;
#endif
        base.OnDisappearing();
    }

#if ANDROID
    private void OnLinkStatusChanged(string status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = status;
        });
    }

    private void OnInputDebug(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            InputLabel.Text = text;
        });
    }
#endif

    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        string host = string.IsNullOrWhiteSpace(HostEntry.Text)
            ? DefaultHost
            : HostEntry.Text.Trim();

        if (!int.TryParse(PortEntry.Text?.Trim(), out int port) || port is <= 0 or > 65535)
        {
            await DisplayAlertAsync("Hata", "Geçerli bir port numarası girin (1-65535).", "Tamam");
            return;
        }

        HostEntry.Text = host;
        Preferences.Default.Set(HostPrefKey, host);
        Preferences.Default.Set(PortPrefKey, port);

#if ANDROID
        var context = (global::Android.Content.Context?)Platform.CurrentActivity ?? Platform.AppContext;
        GamepadBridgeService.StartBridge(context, host, port);
#endif
    }

    private void OnDisconnectClicked(object? sender, EventArgs e)
    {
#if ANDROID
        var context = (global::Android.Content.Context?)Platform.CurrentActivity ?? Platform.AppContext;
        GamepadBridgeService.StopBridge(context);
#endif
    }

    private void OnPinClicked(object? sender, EventArgs e)
    {
        _isPinned = !_isPinned;
        PinButton.Text = _isPinned
            ? "Ekran Sabitlemeyi Kaldır"
            : "Ekranı Sabitle (PS/Home Engelle)";

#if ANDROID
        (Platform.CurrentActivity as MainActivity)?.SetScreenPinning(_isPinned);
#endif
    }

    private void OnEnterDimMode(object? sender, EventArgs e)
    {
        BlackoutOverlay.IsVisible = true;
#if ANDROID
        (Platform.CurrentActivity as MainActivity)?.SetDimMode(true);
#endif
    }

    private void OnExitDimMode(object? sender, TappedEventArgs e)
    {
        BlackoutOverlay.IsVisible = false;
#if ANDROID
        (Platform.CurrentActivity as MainActivity)?.SetDimMode(false);
#endif
    }
}
