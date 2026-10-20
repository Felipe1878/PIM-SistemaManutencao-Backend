using System.Net;
using System.Net.Http;
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
}
