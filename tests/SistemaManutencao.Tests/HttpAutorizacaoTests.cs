using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 2 — Matriz de autorização por recurso: cada perfil abaixo NÃO tem permissão
/// na rota indicada e deve receber 403 com o envelope {"erro": "Você não possui
/// permissão..."} do middleware. A checagem de role ocorre antes do handler, portanto
/// ids inexistentes (ex.: /1) ainda resultam em 403, não 404.
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpAutorizacaoTests
{
    private readonly ApiHttpFixture _api;

    public HttpAutorizacaoTests(ApiHttpFixture api) => _api = api;

    public static TheoryData<string, string, string> CasosMatriz => new()
    {
        // Ocorrências: leitura/criação é Admin/Operador; Técnico não.
        { "tecnico", "GET", "/api/ocorrencias" },
        { "tecnico", "POST", "/api/ocorrencias" },
        // Ordens de serviço: listagem geral/disponíveis é Admin/Técnico; Operador não.
        { "operador", "GET", "/api/ordens-servico" },
        { "operador", "GET", "/api/ordens-servico/disponiveis" },
        // Criação de OS é exclusiva do Admin.
        { "operador", "POST", "/api/ordens-servico" },
        { "tecnico", "POST", "/api/ordens-servico" },
        // Assumir OS é exclusivo do Técnico (nem Admin, nem Operador).
        { "admin", "POST", "/api/ordens-servico/1/assumir" },
        { "operador", "POST", "/api/ordens-servico/1/assumir" },
        // Cancelar OS é exclusivo do Admin.
        { "tecnico", "POST", "/api/ordens-servico/1/cancelar" },
        // Planos de manutenção: somente Admin.
        { "operador", "GET", "/api/planos-manutencao" },
        { "tecnico", "GET", "/api/planos-manutencao" },
        // Dashboard é somente Admin.
        { "operador", "GET", "/api/dashboard" },
        { "tecnico", "GET", "/api/dashboard" },
        // Usuários: somente Admin.
        { "operador", "POST", "/api/usuarios" },
        // Setores: criação somente Admin.
        { "tecnico", "POST", "/api/setores" },
        // Inativação de equipamento somente Admin.
        { "operador", "PATCH", "/api/equipamentos/1/inativar" },
    };

    [Theory, MemberData(nameof(CasosMatriz))]
    public async Task Perfil_sem_permissao_recebe_403_com_corpo_de_erro(string perfil, string metodo, string rota)
    {
        var cliente = await _api.ClienteDoPerfilAsync(perfil);
        using var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota);

        var resposta = await cliente.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("Você não possui permissão para realizar esta ação.", await resposta.ErroAsync());
    }

    [Fact]
    public async Task Admin_nao_pode_rebaixar_a_propria_conta()
    {
        // M-11 (integridade administrativa): auto-rebaixamento deixaria o sistema sem admin
        // (sem reativação via API; seed apenas em banco vazio). PUT em si mesmo mantendo
        // Administrador continua permitido (ex.: atualizar o nome).
        var admin = await _api.AdminAsync();
        var lista = await admin.GetAsync("/api/usuarios");
        Assert.True(lista.IsSuccessStatusCode);
        var id = (await lista.CorpoAsync()).EnumerateArray()
            .Single(u => u.GetProperty("email").GetString() == ApiHttpFixture.AdminEmail)
            .GetProperty("id").GetInt32();

        var rebaixamento = await admin.PutAsJsonAsync($"/api/usuarios/{id}",
            new { nome = "Administrador Teste", perfil = "Funcionario", funcao = "Operador" });
        Assert.Equal(HttpStatusCode.Conflict, rebaixamento.StatusCode);
        Assert.Equal("Não é permitido alterar o perfil da própria conta administrativa.",
            await rebaixamento.ErroAsync());

        var mantendoPerfil = await admin.PutAsJsonAsync($"/api/usuarios/{id}",
            new { nome = "Administrador Teste", perfil = "Administrador" });
        Assert.True(mantendoPerfil.IsSuccessStatusCode,
            $"PUT próprio mantendo Administrador falhou: {(int)mantendoPerfil.StatusCode}");
    }
}
