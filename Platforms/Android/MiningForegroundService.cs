using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using MineDigibyteMobile.Services;
using MineDigibyteMobile.Localization;

namespace MineDigibyteMobile.Platforms.Android;

[Service(
    Name = "com.companyname.minedigibytemobile.MiningForegroundService",
    Exported = false)]
public sealed class MiningForegroundService : Service
{
    public const string ActionUpdate =
        "com.companyname.minedigibytemobile.action.UPDATE";

    private const string ChannelIdPrefix = "mining_foreground";
    private const int NotificationId = 4107;

    private PowerManager.WakeLock? wakeLock;
    private bool foregroundStarted;

    public override void OnCreate()
    {
        base.OnCreate();
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            CreateNotificationChannel();
        MiningSessionRuntime.Current.StatusChanged += OnStatusChanged;
        MiningPreferences.IntensityChanged += OnIntensityChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        string? action = intent?.Action;

        StartForeground(NotificationId, CreateNotification());
        foregroundStarted = true;
        MiningSessionRuntime.Current.SetBackgroundServiceActive(true);
        AcquireWakeLock();

        if (action == ActionUpdate)
            ShowNotification();

        return StartCommandResult.NotSticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnDestroy()
    {
        MiningSessionRuntime.Current.StatusChanged -= OnStatusChanged;
        MiningPreferences.IntensityChanged -= OnIntensityChanged;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        MiningSessionRuntime.Current.SetBackgroundServiceActive(false);

        if (foregroundStarted)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                StopForeground(StopForegroundFlags.Remove);
            else
                StopForeground(true);
            foregroundStarted = false;
        }

        NotificationManagerCompat.From(this)?.Cancel(NotificationId);

        if (wakeLock?.IsHeld == true)
            wakeLock.Release();

        wakeLock?.Dispose();
        wakeLock = null;
        base.OnDestroy();
    }

    private void AcquireWakeLock()
    {
        if (wakeLock?.IsHeld == true)
            return;

        var powerManager =
            (PowerManager?)GetSystemService(PowerService);
        wakeLock = powerManager?.NewWakeLock(
            WakeLockFlags.Partial,
            $"{PackageName}:Mining");
        wakeLock?.Acquire();
    }

    private Notification CreateNotification()
    {
        MiningSessionRuntime runtime = MiningSessionRuntime.Current;
        LocalizationService localization = LocalizationService.Instance;
        string state = localization.TranslateText(runtime.Status);
        string intensityLabel = localization.GetString("NotificationIntensity");
        string content = $"{state} · {intensityLabel}{runtime.IntensityPercent}%";

        var openAppIntent = new Intent(this, typeof(MainActivity));
        openAppIntent.SetFlags(
            ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        PendingIntent openApp = PendingIntent.GetActivity(
            this,
            1,
            openAppIntent,
            GetPendingIntentFlags())!;

        var builder = new NotificationCompat.Builder(this, GetChannelId());
        builder.SetContentTitle(localization.GetString("NotificationTitle"));
        builder.SetContentText(content);
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcMediaPlay);
        builder.SetContentIntent(openApp);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        if (runtime.IsPaused)
        {
            builder.AddAction(
                global::Android.Resource.Drawable.IcMediaPlay,
                localization.GetString("NotificationResume"),
                CreateBroadcastAction(
                    MiningNotificationActionReceiver.ActionResume,
                    2));
        }
        else
        {
            builder.AddAction(
                global::Android.Resource.Drawable.IcMediaPause,
                localization.GetString("NotificationPause"),
                CreateBroadcastAction(
                    MiningNotificationActionReceiver.ActionPause,
                    3));
        }

        builder.AddAction(
            global::Android.Resource.Drawable.IcDelete,
            localization.GetString("NotificationStop"),
            CreateBroadcastAction(
                MiningNotificationActionReceiver.ActionStop,
                4));

        return builder.Build()
            ?? throw new InvalidOperationException(
                localization.GetString("NotificationFailure"));
    }

    private PendingIntent CreateBroadcastAction(
        string action,
        int requestCode)
    {
        var actionIntent = new Intent(
            this,
            typeof(MiningNotificationActionReceiver));
        actionIntent.SetAction(action);
        return PendingIntent.GetBroadcast(
            this,
            requestCode,
            actionIntent,
            GetPendingIntentFlags())!;
    }

    private static PendingIntentFlags GetPendingIntentFlags()
    {
        PendingIntentFlags flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            flags |= PendingIntentFlags.Immutable;
        return flags;
    }

    private void ShowNotification()
    {
        NotificationManagerCompat manager =
            NotificationManagerCompat.From(this)
            ?? throw new InvalidOperationException(
                LocalizationService.Instance.GetString("NotificationManagerUnavailable"));
        manager.Notify(
            NotificationId,
            CreateNotification());
    }

    private void OnStatusChanged(string _) =>
        ShowNotification();

    private void OnIntensityChanged(int _) =>
        ShowNotification();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            CreateNotificationChannel();
        ShowNotification();
    }

    private string GetChannelId() =>
        $"{ChannelIdPrefix}_{LocalizationService.Instance.CurrentLanguage}";

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var channel = new NotificationChannel(
            GetChannelId(),
            LocalizationService.Instance.GetString("NotificationChannelName"),
            NotificationImportance.Low)
        {
            Description = LocalizationService.Instance.GetString(
                "NotificationChannelDescription")
        };

        var manager = (NotificationManager?)GetSystemService(
            NotificationService);
        if (manager == null)
            throw new InvalidOperationException(
                LocalizationService.Instance.GetString("NotificationManagerUnavailable"));

        manager.CreateNotificationChannel(channel);
    }
}
