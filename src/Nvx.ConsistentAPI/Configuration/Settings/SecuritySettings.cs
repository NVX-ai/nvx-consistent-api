using Microsoft.IdentityModel.Tokens;

namespace Nvx.ConsistentAPI.Configuration.Settings;

/// <summary>
/// Settings for security and authentication.
/// </summary>
public record SecuritySettings(
    SecurityKey[] JwtPublicKeys,
    string AdminId,
    string ToolingEndpointsApiKey);
