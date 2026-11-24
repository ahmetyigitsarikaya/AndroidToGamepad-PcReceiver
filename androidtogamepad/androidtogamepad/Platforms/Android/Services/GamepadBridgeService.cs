using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using androidtogamepad.Platforms.Android.Net;

namespace androidtogamepad.Platforms.Android.Services;

/// <summary>
/// Keeps the TCP link to the PC alive in the background with a persistent foreground notification and partial wake lock.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class GamepadBridgeService : Service
{
    private const string ChannelId = "gamepad_bridge";
    private const int NotificationId = 1001;

    public const string ActionStart = "androidtogamepad.ACTION_START";
    public const string ActionStop = "androidtogamepad.ACTION_STOP";
    public const string ExtraHost = "host";
    public const string ExtraPort = "port";

    private PowerManager.WakeLock? _wakeLock;
    private bool _subscribed;

    public static void StartBridge(Context context, string host, int port)
    {
        var intent = new Intent(context, typeof(GamepadBridgeService))
            .SetAction(ActionStart)
            .PutExtra(ExtraHost, host)
            .PutExtra(ExtraPort, port);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public static void StopBridge(Context context)
    {
        PcLink.Instance.Stop();
        context.StopService(new Intent(context, typeof(GamepadBridgeService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            PcLink.Instance.Stop();
            try { StopForeground(StopForegroundFlags.Remove); } catch { }
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        EnsureNotificationChannel();
        Notification notification = BuildNotification(PcLink.Instance.CurrentStatus);

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        AcquireWakeLock();

        if (!_subscribed)
        {
            PcLink.Instance.StatusChanged += OnStatusChanged;
            _subscribed = true;
        }

        if (intent?.Action == ActionStart)
        {
            string host = intent.GetStringExtra(ExtraHost) ?? "127.0.0.1";
            int port = intent.GetIntExtra(ExtraPort, 28555);
            PcLink.Instance.Start(host, port);
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (_subscribed)
        {
            PcLink.Instance.StatusChanged -= OnStatusChanged;
            _subscribed = false;
        }

        if (_wakeLock is { IsHeld: true })
        {
            try { _wakeLock.Release(); } catch { }
        }
        _wakeLock = null;

        base.OnDestroy();
    }

    private void AcquireWakeLock()
    {
        if (_wakeLock is null)
        {
            var pm = GetSystemService(PowerService) as PowerManager;
            _wakeLock = pm?.NewWakeLock(WakeLockFlags.Partial, "androidtogamepad:bridge");
            _wakeLock?.SetReferenceCounted(false);
        }

        if (_wakeLock is { IsHeld: false })
        {
            _wakeLock.Acquire();
        }
    }

    private void EnsureNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = GetSystemService(NotificationService) as NotificationManager;
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
            return;

        var channel = new NotificationChannel(ChannelId, "PS3 → PC Köprüsü", NotificationImportance.Low);
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string status)
    {
        const PendingIntentFlags pendingFlags = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;

        var openIntent = new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop);
        var openPending = PendingIntent.GetActivity(this, 0, openIntent, pendingFlags);

        var stopIntent = new Intent(this, typeof(GamepadBridgeService)).SetAction(ActionStop);
        var stopPending = PendingIntent.GetService(this, 1, stopIntent, pendingFlags);

        int iconRes = ApplicationInfo?.Icon ?? 0;
        if (iconRes == 0)
            iconRes = global::Android.Resource.Drawable.IcDialogInfo;

        Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            builder = new Notification.Builder(this, ChannelId);
        }
        else
        {
#pragma warning disable CA1422, CS0618
            builder = new Notification.Builder(this);
#pragma warning restore CA1422, CS0618
        }

        var stopAction = new Notification.Action.Builder((Icon?)null, "Durdur", stopPending).Build();

        return builder
            .SetContentTitle("PS3 → PC Köprüsü")
            .SetContentText(status)
            .SetOngoing(true)
            .SetSmallIcon(iconRes)
            .SetContentIntent(openPending)
            .AddAction(stopAction)
            .Build()!;
    }

    private void OnStatusChanged(string status)
    {
        try
        {
            var manager = GetSystemService(NotificationService) as NotificationManager;
            manager?.Notify(NotificationId, BuildNotification(status));
        }
        catch
        {
            // Bildirim güncellemesi başarısız olursa servisi kesme.
        }
    }
}
