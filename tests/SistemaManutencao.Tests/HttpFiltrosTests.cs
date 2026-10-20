using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 3 — Filtros de leitura sobre HTTP (busca por texto e por status) e 404 de
/// recurso inexistente.
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpFiltrosTests
{
    private readonly ApiHttpFixture _api;

    public HttpFiltrosTests(ApiHttpFixture api) => _api = api;

    [Fact]
    public async Task Busca_por_palavra_da_descricao_retorna_a_certa_e_exclui_a_errada()
    {
        var (_, _, ocorrenciaCerta) = await _api.CriarCenarioOcorrenciaAsync(
            "F1", "Vazamento na bomba hidraulica da prensa");
        var (_, _, ocorrenciaErrada) = await _api.CriarCenarioOcorrenciaAsync(
            "F2", "Correia do motor solta");

        var admin = await _api.AdminAsync();
        var corpo = await (await admin.GetAsync("/api/ocorrencias?busca=hidraulica")).CorpoAsync();

        Assert.Contains(corpo.EnumerateArray(), o => o.GetProperty("id").GetInt32() == ocorrenciaCerta);
        Assert.DoesNotContain(corpo.EnumerateArray(), o => o.GetProperty("id").GetInt32() == ocorrenciaErrada);
    }

    [Fact]
    public async Task Filtro_por_status_retorna_apenas_ocorrencias_naquele_estado()
    {
        var (_, _, ocorrenciaAberta) = await _api.CriarCenarioOcorrenciaAsync(
            "F3", "Ocorrencia que permanecera aberta para o filtro");

        var admin = await _api.AdminAsync();
        var abertas = await (await admin.GetAsync("/api/ocorrencias?status=Aberta")).CorpoAsync();
        var resolvidas = await (await admin.GetAsync("/api/ocorrencias?status=Resolvida")).CorpoAsync();

        Assert.Contains(abertas.EnumerateArray(), o => o.GetProperty("id").GetInt32() == ocorrenciaAberta);
        Assert.DoesNotContain(resolvidas.EnumerateArray(), o => o.GetProperty("id").GetInt32() == ocorrenciaAberta);
    }

    [Fact]
    public async Task Obter_equipamento_inexistente_retorna_404()
    {
        var admin = await _api.AdminAsync();
        var resposta = await admin.GetAsync("/api/equipamentos/999999");
        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
