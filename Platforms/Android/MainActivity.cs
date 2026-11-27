using Android.App;
using Android.Content.PM;
using Android.OS;
using MineDigibyteMobile.Services;

namespace MineDigibyteMobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
    }

    protected override void OnStop()
    {
        base.OnStop();

        if (!MiningPreferences.BackgroundMiningEnabled &&
            MiningSessionRuntime.Current.IsActive)
        {
            _ = StopMiningAfterLeavingAppAsync();
        }
    }

    private static async Task StopMiningAfterLeavingAppAsync()
    {
        try
        {
            await MiningSessionRuntime.Current.StopAsync();
        }
        catch (Exception exception)
        {
            Android.Util.Log.Error(
                "MineDigibyteMobile",
                "Could not stop mining after app backgrounded: " +
                exception.Message);
        }
    }

}
