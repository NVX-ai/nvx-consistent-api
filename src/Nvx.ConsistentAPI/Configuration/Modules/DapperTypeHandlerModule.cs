using Dapper;
using Microsoft.AspNetCore.Builder;

namespace Nvx.ConsistentAPI.Configuration.Modules;

/// <summary>
/// Module that configures Dapper type handlers for DateTime, DateOnly, and ulong.
/// </summary>
public class DapperTypeHandlerModule : IGeneratorModule
{
    public int Order => 0;

    public void ConfigureServices(WebApplicationBuilder builder, GeneratorSettings settings, EventModel eventModel)
    {
        SqlMapper.RemoveTypeMap(typeof(DateTime));
        SqlMapper.RemoveTypeMap(typeof(DateTime?));
        SqlMapper.RemoveTypeMap(typeof(ulong));
        SqlMapper.RemoveTypeMap(typeof(ulong?));
        SqlMapper.AddTypeHandler(typeof(DateTime), new DateTimeTypeHandler());
        SqlMapper.AddTypeHandler(typeof(DateTime?), new DateTimeTypeHandler());
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new DateOnlyNullableTypeHandler());
        SqlMapper.AddTypeHandler(new ULongTypeHandler());
        SqlMapper.AddTypeHandler(new ULongNullableTypeHandler());
    }

    public void ConfigureApp(WebApplication app, GeneratorSettings settings, EventModel eventModel)
    {
        // No app configuration needed
    }
}
