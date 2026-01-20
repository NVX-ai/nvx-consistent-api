using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI.Configuration.Modules.Swagger;

// ReSharper disable once ClassNeverInstantiated.Global
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema model, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
        {
            return;
        }

        model.Type = "string";
        model.Enum.Clear();

        foreach (var n in Enum.GetNames(context.Type))
        {
            model.Enum.Add(new OpenApiString(n));
        }
    }
}
