using Xunit;

namespace SistemaManutencao.Tests;

[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpOpenApiErrosTests(ApiHttpFixture api)
{
    [Theory]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("409")]
    public async Task OpenAPI_descreve_envelope_erro_dos_status_de_negocio(string status)
    {
        using var cliente = api.ClienteAnonimo();
        var documento = await (await cliente.GetAsync("/swagger/v1/swagger.json")).CorpoAsync();
        var schema = documento.GetProperty("paths").GetProperty("/api/usuarios").GetProperty("post")
            .GetProperty("responses").GetProperty(status).GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/ErroResposta", schema.GetProperty("$ref").GetString());
        var erro = documento.GetProperty("components").GetProperty("schemas").GetProperty("ErroResposta");
        Assert.True(erro.GetProperty("properties").TryGetProperty("erro", out _));
        Assert.Contains(erro.GetProperty("required").EnumerateArray(), p => p.GetString() == "erro");
    }

    [Fact]
    public async Task OpenAPI_descreve_os_dois_envelopes_possiveis_em_400()
    {
        using var cliente = api.ClienteAnonimo();
        var documento = await (await cliente.GetAsync("/swagger/v1/swagger.json")).CorpoAsync();
        var schema = documento.GetProperty("paths").GetProperty("/api/usuarios").GetProperty("post")
            .GetProperty("responses").GetProperty("400").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        Assert.True(schema.TryGetProperty("anyOf", out var opcoes), "400 precisa descrever validacao e erro de negocio.");
        var referencias = opcoes.EnumerateArray().Select(x => x.GetProperty("$ref").GetString()).ToArray();
        Assert.Contains("#/components/schemas/ErroResposta", referencias);
        Assert.Contains("#/components/schemas/ValidationProblemDetails", referencias);
    }
}
