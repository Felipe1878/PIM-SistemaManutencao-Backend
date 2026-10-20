using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 3 — 400 de enum obrigatório (prova o fix do Sub A): os DTOs usam enums
/// anuláveis com [Required]; a omissão do campo no JSON é barrada com 400 antes
/// de qualquer efeito no banco (o default silencioso não existe mais).
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpValidacaoEnumTests
{
    private readonly ApiHttpFixture _api;

    public HttpValidacaoEnumTests(ApiHttpFixture api) => _api = api;

    [Fact]
    public async Task Post_usuarios_sem_perfil_retorna_400_e_nao_cria_usuario()
    {
        var admin = await _api.AdminAsync();
        var contagemAntes = await ContarUsuariosAsync(admin);

        var resposta = await admin.PostAsJsonAsync("/api/usuarios",
            new { nome = "Usuario Sem Perfil", email = "semperfil@teste.local", senha = ApiHttpFixture.SenhaPadrao });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var contagemDepois = await ContarUsuariosAsync(admin);
        Assert.Equal(contagemAntes, contagemDepois);
    }

    [Fact]
    public async Task Post_ocorrencias_sem_prioridade_retorna_400()
    {
        var (_, equipamentoId, _) = await _api.CriarCenarioOcorrenciaAsync("C1", "Cenario de validacao do enum prioridade");

        var operador = await _api.OperadorAsync();
        var resposta = await operador.PostAsJsonAsync("/api/ocorrencias",
            new { equipamentoId, descricao = "Ocorrencia sem prioridade informada" });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Post_ordens_servico_sem_tipoManutencao_retorna_400()
    {
        var (_, _, ocorrenciaId) = await _api.CriarCenarioOcorrenciaAsync("C2", "Cenario de validacao do enum tipoManutencao");

        var admin = await _api.AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/ordens-servico", new { ocorrenciaId });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    private static async Task<int> ContarUsuariosAsync(HttpClient admin)
    {
        var resposta = await admin.GetAsync("/api/usuarios");
        resposta.EnsureSuccessStatusCode();
        return (await resposta.CorpoAsync()).GetArrayLength();
    }
}
