namespace Nvx.ConsistentAPI;

public record JsonLogicValidationResult(string[] Errors)
{
  public Result<Unit, ApiError> ToResult() =>
    Errors.Length != 0
      ? new ValidationError(Errors)
      : unit;
}
