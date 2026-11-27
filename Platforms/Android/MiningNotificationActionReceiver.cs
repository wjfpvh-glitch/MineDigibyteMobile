using Android.App;
using Android.Content;
using AndroidX.Core.App;
using MineDigibyteMobile.Services;

namespace MineDigibyteMobile.Platforms.Android;

[BroadcastReceiver(
    Name = "com.companyname.minedigibytemobile.MiningNotificationActionReceiver",
    Enabled = true,
    Exported = false)]
[IntentFilter(new[]
{
    MiningNotificationActionReceiver.ActionPause,
    MiningNotificationActionReceiver.ActionResume,
    MiningNotificationActionReceiver.ActionStop
})]
public sealed class MiningNotificationActionReceiver : BroadcastReceiver
{
    public const string ActionPause =
        "com.companyname.minedigibytemobile.action.PAUSE";
    public const string ActionResume =
        "com.companyname.minedigibytemobile.action.RESUME";
    public const string ActionStop =
        "com.companyname.minedigibytemobile.action.STOP";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action is not string action)
        {
            global::Android.Util.Log.Error(
                "MineDigibyteMobile",
                "Ação da notificação recebida sem contexto ou action.");
            return;
        }

        if (action == ActionStop)
        {
            // O botão da notificação executa um BroadcastReceiver: não abre
            // MainActivity e, portanto, não traz a interface para a frente.
            MiningPreferences.BackgroundMiningEnabled = false;
            var pending = GoAsync();
            _ = StopMiningAsync(context.ApplicationContext, pending);
            return;
        }

        MiningSessionRuntime runtime = MiningSessionRuntime.Current;
        switch (action)
        {
            case ActionPause:
                runtime.SetPaused(true);
                break;

            case ActionResume:
                runtime.SetPaused(false);
                break;

            default:
                global::Android.Util.Log.Warn(
                    "MineDigibyteMobile",
                    $"Ação da notificação desconhecida: {action}");
                break;
        }
    }

    private static async Task StopMiningAsync(
        Context context,
        BroadcastReceiver.PendingResult pending)
    {
        try
        {
            await MiningSessionRuntime.Current.StopAsync();
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error(
                "MineDigibyteMobile",
                "Falha ao parar o minerador pela notificação: " +
                exception.Message);
        }
        finally
        {
            try
            {
                var serviceIntent = new Intent(
                    context, typeof(MiningForegroundService));
                context.StopService(serviceIntent);
                NotificationManagerCompat.From(context)?.Cancel(4107);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Error(
                    "MineDigibyteMobile",
                    "Falha ao encerrar o serviço da notificação: " +
                    exception.Message);
            }
            finally
            {
                pending.Finish();
            }
        }
    }
}
