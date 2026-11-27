namespace MineDigibyteMobile.Services;

#if ANDROID
using MineDigibyteMobile.Platforms.Android;
#endif

public static class MiningBackgroundServiceControl
{
    public static void Start()
    {
#if ANDROID
        Android.Content.Context context =
            Android.App.Application.Context;
        var intent = new Android.Content.Intent(
            context,
            typeof(MiningForegroundService));
        AndroidX.Core.Content.ContextCompat.StartForegroundService(
            context,
            intent);
#endif
    }

    public static void RefreshNotification()
    {
#if ANDROID
        Android.Content.Context context =
            Android.App.Application.Context;
        var intent = new Android.Content.Intent(
            context,
            typeof(MiningForegroundService));
        intent.SetAction(
            MiningForegroundService.ActionUpdate);
        context.StartService(intent);
#endif
    }

    public static void Stop()
    {
#if ANDROID
        Android.Content.Context context =
            Android.App.Application.Context;
        var intent = new Android.Content.Intent(
            context,
            typeof(MiningForegroundService));
        context.StopService(intent);
#endif
    }
}
