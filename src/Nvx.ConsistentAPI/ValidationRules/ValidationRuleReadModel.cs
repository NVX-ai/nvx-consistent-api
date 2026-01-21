namespace Nvx.ConsistentAPI.ValidationRules;

internal record ValidationRuleReadModel(string Id, string CommandName, string[] Rules) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(CommandName);
}
