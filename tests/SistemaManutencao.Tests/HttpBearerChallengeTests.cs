using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace SistemaManutencao.Tests;

[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpBearerChallengeTests(ApiHttpFixture api)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Endpoint_protegido_retorna_desafio_Bearer_em_401(bool tokenInvalido)
    {
        using var cliente = api.ClienteAnonimo();
        if (tokenInvalido)
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        var resposta = await cliente.GetAsync("/api/equipamentos");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Contains(resposta.Headers.WwwAuthenticate, desafio => desafio.Scheme == "Bearer");
        Assert.Contains("Autenticação necessária", await resposta.ErroAsync());
    }
}
