namespace Nvx.ConsistentAPI;

public interface InterestTrigger
{
  EntityInterestManifest[] Initiates(EventModelEvent evt);
  EntityInterestManifest[] Stops(EventModelEvent evt);
}

public abstract class InterestTrigger<T> : InterestTrigger where T : EventModelEvent
{
  public EntityInterestManifest[] Initiates(EventModelEvent evt) => evt is T t ? Initiates(t) : [];
  public EntityInterestManifest[] Stops(EventModelEvent evt) => evt is T t ? Stops(t) : [];
  protected abstract EntityInterestManifest[] Initiates(T evt);
  protected abstract EntityInterestManifest[] Stops(T evt);
}

public class TriggersInterest<T>(
  Func<T, EntityInterestManifest[]> initiates,
  Func<T, EntityInterestManifest[]> stops) : InterestTrigger<T> where T : EventModelEvent
{
  protected override EntityInterestManifest[] Initiates(T evt) => initiates(evt);
  protected override EntityInterestManifest[] Stops(T evt) => stops(evt);
}

public class InitiatesInterest<T>(Func<T, EntityInterestManifest[]> initiates)
  : InterestTrigger<T> where T : EventModelEvent
{
  protected override EntityInterestManifest[] Initiates(T evt) => initiates(evt);
  protected override EntityInterestManifest[] Stops(T evt) => [];
}

public class StopsInterest<T>(Func<T, EntityInterestManifest[]> stops)
  : InterestTrigger<T> where T : EventModelEvent
{
  protected override EntityInterestManifest[] Initiates(T evt) => [];
  protected override EntityInterestManifest[] Stops(T evt) => stops(evt);
}
