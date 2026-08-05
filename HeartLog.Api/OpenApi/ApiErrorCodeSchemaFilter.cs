using HeartLog.Api.DTOs;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text.Json;

namespace HeartLog.Api.OpenApi;

public class ApiErrorCodeSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ApiErrorCode))
        {
            return;
        }

        schema.Type = "string";
        schema.Format = null;
        schema.Enum.Clear();

        foreach (var value in Enum.GetNames<ApiErrorCode>())
        {
            schema.Enum.Add(new OpenApiString(JsonNamingPolicy.CamelCase.ConvertName(value)));
        }
    }
}
