using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpRevogacaoTests(ApiHttpFixture api)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_emitido_perde_acesso_apos_inativacao_ou_mudanca_de_funcao(bool mudarFuncao)
    {
        var admin = await api.AdminAsync();
        var email = $"revogacao-{mudarFuncao}@teste.local";
        var criacao = await admin.PostAsJsonAsync("/api/usuarios", new
        {
            nome = "Usuario Revogacao", email, senha = ApiHttpFixture.SenhaPadrao,
            perfil = "Funcionario", funcao = "Operador"
        });
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var id = (await criacao.CorpoAsync()).GetProperty("id").GetInt32();
        var usuario = await api.ClienteAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await usuario.GetAsync("/api/auth/me")).StatusCode);
        var alteracao = mudarFuncao
            ? await admin.PutAsJsonAsync($"/api/usuarios/{id}", new
                { nome = "Usuario Revogacao", perfil = "Funcionario", funcao = "Tecnico" })
            : await admin.PatchAsync($"/api/usuarios/{id}/inativar", null);
        Assert.Equal(HttpStatusCode.OK, alteracao.StatusCode);
        var resposta = await usuario.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Contains("Autenticação necessária", await resposta.ErroAsync());
    }
}
