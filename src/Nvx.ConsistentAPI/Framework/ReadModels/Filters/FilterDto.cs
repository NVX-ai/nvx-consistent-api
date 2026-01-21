using Microsoft.OpenApi.Models;

namespace Nvx.ConsistentAPI;

// ReSharper disable once NotAccessedPositionalProperty.Global
public record FilterDto(string FieldName, string Key, string Description, OpenApiSchema Schema);
