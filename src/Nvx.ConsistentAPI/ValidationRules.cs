using System.Text.Json;
using Json.Logic;

namespace Nvx.ConsistentAPI;

public partial record FrameworkValidationRuleEntity(string CommandName, string[] Rules)
  : EventModelEntity<FrameworkValidationRuleEntity>,
    Folds<ValidationRuleSet, FrameworkValidationRuleEntity>,
    Folds<ValidationRuleRemoved, FrameworkValidationRuleEntity>
{
  public const string StreamPrefix = "framework-validation-rule-";

  internal static readonly EventModel Get =
    new()
    {
      Entities =
      [
        new EntityDefinition<FrameworkValidationRuleEntity, StrongString>
        {
          Defaulter = Defaulted, StreamPrefix = StreamPrefix
        }
      ],
      ReadModels =
      [
        new ReadModelDefinition<ValidationRuleReadModel, FrameworkValidationRuleEntity>
        {
          StreamPrefix = StreamPrefix,
          Projector = rule => [new ValidationRuleReadModel(rule.CommandName, rule.CommandName, rule.Rules)],
          Auth = new PermissionsRequireOne("validation-rules-management", "validation-rules-read"),
          AreaTag = OperationTags.ValidationRulesManagement
        }
      ],
      Commands =
      [
        new CommandDefinition<SetValidationRule, FrameworkValidationRuleEntity>
        {
          Auth = new PermissionsRequireOne("validation-rules-management"),
          AreaTag = OperationTags.ValidationRulesManagement
        },
        new CommandDefinition<RemoveValidationRule, FrameworkValidationRuleEntity>
        {
          Auth = new PermissionsRequireOne("validation-rules-management"),
          AreaTag = OperationTags.ValidationRulesManagement
        }
      ]
    };

  public Rule[] JsonLogicRules
  {
    get
    {
      var rules = Rules
        .Choose(r =>
        {
          try
          {
            return Some(JsonSerializer.Deserialize<Rule>(r)!);
          }
          catch
          {
            return None;
          }
        })
        .ToArray();
      return rules.Length != 0 ? rules : [JsonSerializer.Deserialize<Rule>("[]")!];
    }
  }

  public string GetStreamName() => GetStreamName(CommandName);

  public ValueTask<FrameworkValidationRuleEntity> Fold(
    ValidationRuleRemoved evt,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Rules = Rules.Where(r => r != evt.Rule).ToArray() });

  public ValueTask<FrameworkValidationRuleEntity> Fold(
    ValidationRuleSet vrs,
    EventMetadata metadata,
    RevisionFetcher fetcher) =>
    ValueTask.FromResult(this with { Rules = Rules.Append(vrs.Rule).Distinct().ToArray() });

  public static string GetStreamName(string commandName) => $"{StreamPrefix}{commandName}";

  public static FrameworkValidationRuleEntity Defaulted(StrongString id) => new(id.Value, []);
}

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

public record ValidationRuleSet(string CommandName, string Rule) : EventModelEvent
{
  public string GetSwimlane() => FrameworkValidationRuleEntity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(CommandName);
}

public record ValidationRuleRemoved(string CommandName, string Rule) : EventModelEvent
{
  public string GetSwimlane() => FrameworkValidationRuleEntity.StreamPrefix;
  public StrongId GetEntityId() => new StrongString(CommandName);
}

internal record ValidationRuleReadModel(string Id, string CommandName, string[] Rules) : EventModelReadModel
{
  public StrongId GetStrongId() => new StrongString(CommandName);
}
