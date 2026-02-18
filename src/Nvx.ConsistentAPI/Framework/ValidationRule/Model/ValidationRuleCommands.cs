namespace Nvx.ConsistentAPI.Model;

public record SetValidationRule(string CommandName, string Rule) : EventModelCommand<FrameworkValidationRuleEntity>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(CommandName);

  public Result<EventInsertion, ApiError> Decide(
    Option<FrameworkValidationRuleEntity> fvr,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ValidationRuleSet(CommandName, Rule));

  public IEnumerable<string> Validate() => [];
}

public record RemoveValidationRule(string CommandName, string Rule) : EventModelCommand<FrameworkValidationRuleEntity>
{
  public Option<StrongId> TryGetEntityId(Option<UserSecurity> user) => new StrongString(CommandName);

  public Result<EventInsertion, ApiError> Decide(
    Option<FrameworkValidationRuleEntity> fvr,
    Option<UserSecurity> user,
    FileUpload[] files
  ) =>
    new AnyState(new ValidationRuleRemoved(CommandName, Rule));

  public IEnumerable<string> Validate() => [];
}