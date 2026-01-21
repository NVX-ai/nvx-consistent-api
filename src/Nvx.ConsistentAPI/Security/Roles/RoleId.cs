namespace Nvx.ConsistentAPI;

public record RoleId(Guid Id, Guid TenantId) : StrongId
{
  public override string StreamId() => ToString();
  public override string ToString() => $"{Id}-{TenantId}";
}
