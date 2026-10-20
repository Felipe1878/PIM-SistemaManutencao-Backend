using System.Net;
using System.Net.Http;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 2 — CORS sobre HTTP: origem permitida (http://localhost:5173, vinda de
/// appsettings.Development.json) recebe Access-Control-Allow-Origin específico;
/// preflight OPTIONS recebe 204 + ACAO; origem não configurada não recebe CORS.
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpCorsTests
{
    private const string OrigemPermitida = "http://localhost:5173";
    private const string OrigemNaoPermitida = "http://evil.test";

    private readonly ApiHttpFixture _api;

    public HttpCorsTests(ApiHttpFixture api) => _api = api;

    [Fact]
    public async Task Health_com_origin_permitida_recebe_header_cors_especifico()
    {
        var cliente = _api.ClienteAnonimo();
        cliente.DefaultRequestHeaders.Add("Origin", OrigemPermitida);

        var resposta = await cliente.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(OrigemPermitida, resposta.Headers.GetValues("Access-Control-Allow-Origin").SingleOrDefault());
    }

    [Fact]
    public async Task Preflight_OPTIONS_com_origin_permitida_retorna_204_com_header_cors()
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Options, "/api/health");
        requisicao.Headers.Add("Origin", OrigemPermitida);
        requisicao.Headers.Add("Access-Control-Request-Method", "GET");

        var resposta = await _api.ClienteAnonimo().SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(OrigemPermitida, resposta.Headers.GetValues("Access-Control-Allow-Origin").SingleOrDefault());
    }

    [Fact]
    public async Task Health_com_origin_nao_permitida_nao_recebe_header_cors()
    {
        var cliente = _api.ClienteAnonimo();
        cliente.DefaultRequestHeaders.Add("Origin", OrigemNaoPermitida);

        var resposta = await cliente.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.False(resposta.Headers.Contains("Access-Control-Allow-Origin"),
            "Origem não configurada não deve receber Access-Control-Allow-Origin.");
    }
}
