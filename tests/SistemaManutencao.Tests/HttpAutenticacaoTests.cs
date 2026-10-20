using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 2 — Autenticação real sobre HTTP: login válido emite JWT; credenciais erradas
/// e requisições sem token devolvem 401 com o envelope {"erro": ...} do middleware.
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpAutenticacaoTests
{
    private readonly ApiHttpFixture _api;

    public HttpAutenticacaoTests(ApiHttpFixture api) => _api = api;

    [Fact]
    public async Task Login_com_credenciais_validas_retorna_200_com_token_e_perfil_administrador()
    {
        var resposta = await _api.ClienteAnonimo().PostAsJsonAsync("/api/auth/login",
            new { email = ApiHttpFixture.AdminEmail, senha = ApiHttpFixture.SenhaPadrao });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.CorpoAsync();
        var token = corpo.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token), "Token não deve ser vazio.");
        Assert.Equal("Bearer", corpo.GetProperty("tipo").GetString());
        Assert.Equal("Administrador", corpo.GetProperty("usuario").GetProperty("perfil").GetString());
    }

    [Fact]
    public async Task Login_com_senha_errada_retorna_401_com_corpo_de_erro()
    {
        var resposta = await _api.ClienteAnonimo().PostAsJsonAsync("/api/auth/login",
            new { email = ApiHttpFixture.AdminEmail, senha = "SenhaTotalmenteErrada123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        var erro = await resposta.ErroAsync();
        Assert.Equal("Email ou senha inválidos.", erro);
    }

    [Fact]
    public async Task Requisicao_sem_token_retorna_401_com_corpo_de_erro_do_middleware()
    {
        var resposta = await _api.ClienteAnonimo().GetAsync("/api/equipamentos");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        var erro = await resposta.ErroAsync();
        Assert.Contains("Autenticação necessária", erro);
    }
}
