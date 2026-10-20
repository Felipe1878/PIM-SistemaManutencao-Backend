using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 3 — Conflitos de negócio (409) com o envelope {"erro": ...}:
/// ocorrência duplicada no mesmo equipamento, segunda OS para a mesma ocorrência
/// (relação 1:1) e segundo técnico tentando assumir OS já assumida.
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpConflitoNegocioTests
{
    private readonly ApiHttpFixture _api;

    public HttpConflitoNegocioTests(ApiHttpFixture api) => _api = api;

    [Fact]
    public async Task Segunda_ocorrencia_para_o_mesmo_equipamento_retorna_409()
    {
        var (_, equipamentoId, _) = await _api.CriarCenarioOcorrenciaAsync("D1", "Primeira ocorrencia do equipamento D1");

        var operador = await _api.OperadorAsync();
        var resposta = await operador.PostAsJsonAsync("/api/ocorrencias",
            new { equipamentoId, descricao = "Tentativa de segunda ocorrencia no mesmo equipamento", prioridade = "Media" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Contains("ocorrência ativa", await resposta.ErroAsync());
    }

    [Fact]
    public async Task Segunda_OS_para_a_mesma_ocorrencia_retorna_409()
    {
        var (_, _, ocorrenciaId) = await _api.CriarCenarioOcorrenciaAsync("D2", "Ocorrencia para o teste de OS duplicada");

        var admin = await _api.AdminAsync();
        var primeira = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        var segunda = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Contains("já possui OS", await segunda.ErroAsync());
    }

    [Fact]
    public async Task Segundo_tecnico_nao_consegue_assumir_OS_ja_assumida_retorna_409()
    {
        var (_, _, ocorrenciaId) = await _api.CriarCenarioOcorrenciaAsync("D3", "Ocorrencia para o teste de tecnico duplicado");

        var admin = await _api.AdminAsync();
        // Segundo técnico criado pela própria API (o seed tem apenas um técnico).
        var criacaoTecnicoB = await admin.PostAsJsonAsync("/api/usuarios",
            new { nome = "Tecnico B", email = "tecnico-b@teste.local", senha = ApiHttpFixture.SenhaPadrao,
                perfil = "Funcionario", funcao = "Tecnico" });
        Assert.Equal(HttpStatusCode.Created, criacaoTecnicoB.StatusCode);

        var criacaoOs = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Created, criacaoOs.StatusCode);
        var osId = (await criacaoOs.CorpoAsync()).GetProperty("id").GetInt32();

        var tecnicoA = await _api.TecnicoAsync();
        var assumirA = await tecnicoA.PostAsync($"/api/ordens-servico/{osId}/assumir", null);
        Assert.Equal(HttpStatusCode.OK, assumirA.StatusCode);

        var tecnicoB = await _api.ClienteAsync("tecnico-b@teste.local");
        var assumirB = await tecnicoB.PostAsync($"/api/ordens-servico/{osId}/assumir", null);

        Assert.Equal(HttpStatusCode.Conflict, assumirB.StatusCode);
        Assert.Contains("ocupada", await assumirB.ErroAsync());
    }
}
