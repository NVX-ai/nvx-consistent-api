namespace Nvx.ConsistentAPI;

public record UserWithPermissionId(string Sub, string Permission) : StrongId
{
  public override string StreamId() => $"{Sub}#{Permission}";
  public override string ToString() => StreamId();
}
