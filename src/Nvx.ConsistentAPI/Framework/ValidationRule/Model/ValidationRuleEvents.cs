namespace Nvx.ConsistentAPI.Model;

public record ValidationRuleSet(string CommandName, string Rule) : EventModelEvent
{
  public string GetStreamName() => FrameworkValidationRuleEntity.GetStreamName(CommandName);
  public StrongId GetEntityId() => new StrongString(CommandName);
}

public record ValidationRuleRemoved(string CommandName, string Rule) : EventModelEvent
{
  public string GetStreamName() => FrameworkValidationRuleEntity.GetStreamName(CommandName);
  public StrongId GetEntityId() => new StrongString(CommandName);
}