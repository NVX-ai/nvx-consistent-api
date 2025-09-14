namespace Nvx.ConsistentAPI.Framework.DaemonCoordination;

public class MessageHub
{
  private event WakeUpHydrationWorker? WakeUpWorker;

  public void Subscribe(HydrationDaemonWorker worker) => WakeUpWorker += worker.WakeUp;

  public void WakeUpHydrationWorkers()
  {
    if (WakeUpWorker is { } wakeyWakey)
    {
      wakeyWakey.Invoke();
    }
  }

  private delegate void WakeUpHydrationWorker();
}
