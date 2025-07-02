using Newtonsoft.Json;

namespace Nvx.ConsistentAPI;

public class OpenIdConfiguration
{
  public OpenIdConfiguration(string issuer, string jwksUri)
  {
    Issuer = issuer;
    JwksUri = jwksUri;
  }

  [JsonProperty("issuer")] public string Issuer { get; set; }

  [JsonProperty("jwks_uri")] public string JwksUri { get; set; }
}
