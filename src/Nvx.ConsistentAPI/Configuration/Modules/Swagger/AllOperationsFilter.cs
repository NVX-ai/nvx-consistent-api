using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Nvx.ConsistentAPI.Configuration.Modules.Swagger;

// ReSharper disable once ClassNeverInstantiated.Global
internal class AllOperationsFilter(EventModel model) : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Tags ??= new List<OpenApiTag>();
        if (!operation.Tags.Any())
        {
            operation.Tags.Add(new OpenApiTag { Name = $"NotTagged{model.ApiName?.Replace(" ", "")}" });
        }
    }
}
