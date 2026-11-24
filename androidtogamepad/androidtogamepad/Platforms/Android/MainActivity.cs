using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using androidtogamepad.Platforms.Android.Hid;
using androidtogamepad.Platforms.Android.Input;
using androidtogamepad.Platforms.Android.Net;

namespace androidtogamepad;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int NotificationPermissionRequestCode = 2001;

    public static event Action<string>? InputDebug;

    private readonly XboxGamepadState _state = new();
    private readonly Ps3InputMapper _mapper = new();
    private long _lastDebugTicks;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions([global::Android.Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
        }
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (Ps3InputMapper.IsGamepadEvent(e))
        {
            if (_mapper.HandleKey(e!, _state))
            {
                var packet = _state.ToPacket();
                PcLink.Instance.Send(packet);
                EmitDebug(e!.Device?.Name, packet, PcLink.Instance.IsConnected);
            }
            // Tüm kol tuşlarını yutarak telefon arayüzünde odak kaymasını veya geri/ana ekran tetiklenmesini engelle.
            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (Ps3InputMapper.IsGamepadEvent(e))
        {
            if (_mapper.HandleMotion(e!, _state))
            {
                var packet = _state.ToPacket();
                PcLink.Instance.Send(packet);
                EmitDebug(e!.Device?.Name, packet, PcLink.Instance.IsConnected);
            }
            // Tüm kol eksen hareketlerini yut; telefon tarafından işlenmesin.
            return true;
        }

        return base.DispatchGenericMotionEvent(e);
    }

    public void SetScreenPinning(bool enable)
    {
        try
        {
            if (enable)
                StartLockTask();
            else
                StopLockTask();
        }
        catch
        {
            // Ekran sabitleme sistem ayarlarından devre dışıysa veya zaten durumdaysa yoksay.
        }
    }

    public void SetDimMode(bool dim)
    {
        if (Window?.Attributes is not { } attrs)
            return;

        attrs.ScreenBrightness = dim ? 0.01f : -1f;
        Window.Attributes = attrs;
    }

    private void EmitDebug(string? deviceName, byte[] packet, bool connected)
    {
        long now = System.Environment.TickCount64;
        if (now - _lastDebugTicks >= 100)
        {
            _lastDebugTicks = now;
            InputDebug?.Invoke($"Kol: {deviceName ?? "?"}\nPaket: {Convert.ToHexString(packet)}\nPC bağlantısı: {(connected ? "OK" : "yok")}");
        }
    }
}
