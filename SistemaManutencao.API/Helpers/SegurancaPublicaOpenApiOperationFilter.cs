using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SistemaManutencao.API.Helpers;

public sealed class SegurancaPublicaOpenApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var publico = context.MethodInfo?.GetCustomAttributes(typeof(IAllowAnonymous), inherit: true).Length > 0
            || context.ApiDescription.ActionDescriptor.EndpointMetadata?.OfType<IAllowAnonymous>().Any() == true;
        if (publico)
            operation.Security = new List<OpenApiSecurityRequirement>();
        else
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecurityScheme
                    { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() }
            };
    }
}
