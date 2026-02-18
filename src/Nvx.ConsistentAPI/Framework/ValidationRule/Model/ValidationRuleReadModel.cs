namespace Nvx.ConsistentAPI.Model;

internal record ValidationRuleReadModel(string Id, string CommandName, string[] Rules) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(CommandName);
}
