using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using SistemaManutencao.API.DTOs;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SistemaManutencao.API.Helpers;

// Ajusta apenas a documentação: não modifica o envelope HTTP nem adota ProblemDetails globalmente.
public sealed class ErrosHttpOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var erro = context.SchemaGenerator.GenerateSchema(typeof(ErroResposta), context.SchemaRepository);
        foreach (var (status, resposta) in operation.Responses)
        {
            if (status is not ("400" or "401" or "403" or "409")) continue;
            var schema = status == "400"
                ? new OpenApiSchema { AnyOf = new List<OpenApiSchema>
                    {
                        erro,
                        context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository)
                    } }
                : erro;
            resposta.Content.Clear();
            resposta.Content["application/json"] = new OpenApiMediaType { Schema = schema };
        }
    }
}
