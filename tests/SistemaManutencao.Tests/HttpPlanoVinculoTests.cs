using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SistemaManutencao.Tests;

[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpPlanoVinculoTests(ApiHttpFixture api)
{
    [Theory]
    [InlineData("Aberta")]
    [InlineData("EmAtendimento")]
    [InlineData("Concluida")]
    public async Task Plano_com_OS_nao_pode_redirecionar_o_equipamento_da_manutencao(string fase)
    {
        var admin = await api.AdminAsync();
        var tecnico = await api.TecnicoAsync();
        var sufixo = $"vinculo-{fase}";
        var setor = await api.CriarSetorAsync($"Setor {sufixo}");
        var original = await api.CriarEquipamentoAsync(setor, "Original", $"{sufixo}-A");
        var outro = await api.CriarEquipamentoAsync(setor, "Outro", $"{sufixo}-B");
        var planoResposta = await admin.PostAsJsonAsync("/api/planos-manutencao", Dados(original));
        Assert.Equal(HttpStatusCode.Created, planoResposta.StatusCode);
        var planoId = (await planoResposta.CorpoAsync()).GetProperty("id").GetInt32();
        var osResposta = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Preventiva", planoManutencaoId = planoId });
        Assert.Equal(HttpStatusCode.Created, osResposta.StatusCode);
        var osId = (await osResposta.CorpoAsync()).GetProperty("id").GetInt32();
        if (fase != "Aberta")
        {
            Assert.Equal(HttpStatusCode.OK, (await tecnico.PostAsync($"/api/ordens-servico/{osId}/assumir", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await tecnico.PostAsync($"/api/ordens-servico/{osId}/iniciar", null)).StatusCode);
        }
        if (fase == "Concluida")
            await ConcluirAsync(tecnico, osId);

        var troca = await admin.PutAsJsonAsync($"/api/planos-manutencao/{planoId}", Dados(outro));
        Assert.Equal(HttpStatusCode.Conflict, troca.StatusCode);
        Assert.Equal("Plano com OS vinculada não pode trocar de equipamento.", await troca.ErroAsync());
        var plano = await (await admin.GetAsync($"/api/planos-manutencao/{planoId}")).CorpoAsync();
        Assert.Equal(original, plano.GetProperty("equipamentoId").GetInt32());

        // Editar a descrição no mesmo equipamento permanece permitido.
        var edicao = await admin.PutAsJsonAsync($"/api/planos-manutencao/{planoId}", Dados(original));
        Assert.Equal(HttpStatusCode.OK, edicao.StatusCode);
        if (fase == "EmAtendimento")
        {
            await ConcluirAsync(tecnico, osId);
            var equipamento = await (await admin.GetAsync($"/api/equipamentos/{original}")).CorpoAsync();
            Assert.Equal("Funcionando", equipamento.GetProperty("status").GetString());
        }
        var intacto = await (await admin.GetAsync($"/api/equipamentos/{outro}")).CorpoAsync();
        Assert.Equal("Funcionando", intacto.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Plano_sem_OS_permite_corrigir_o_equipamento()
    {
        var admin = await api.AdminAsync();
        var setor = await api.CriarSetorAsync("Setor plano sem OS");
        var original = await api.CriarEquipamentoAsync(setor, "Original", "PLANO-LIVRE-A");
        var outro = await api.CriarEquipamentoAsync(setor, "Outro", "PLANO-LIVRE-B");
        var criacao = await admin.PostAsJsonAsync("/api/planos-manutencao", Dados(original));
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var id = (await criacao.CorpoAsync()).GetProperty("id").GetInt32();
        var resposta = await admin.PutAsJsonAsync($"/api/planos-manutencao/{id}", Dados(outro));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var persistido = await (await admin.GetAsync($"/api/planos-manutencao/{id}")).CorpoAsync();
        Assert.Equal(outro, persistido.GetProperty("equipamentoId").GetInt32());
    }

    [Theory]
    [InlineData("9999-12-31T23:59:59Z", false)]
    [InlineData("9999-12-01T23:59:59Z", true)]
    public async Task Preventiva_valida_limite_da_proxima_execucao_sem_gravar_conclusao_invalida(
        string dataConclusao, bool valida)
    {
        var admin = await api.AdminAsync();
        var tecnico = await api.TecnicoAsync();
        var setor = await api.CriarSetorAsync($"Setor limite {valida}");
        var equipamentoId = await api.CriarEquipamentoAsync(setor, "Equipamento limite", $"LIMITE-{valida}");
        var planoResposta = await admin.PostAsJsonAsync("/api/planos-manutencao", Dados(equipamentoId));
        Assert.Equal(HttpStatusCode.Created, planoResposta.StatusCode);
        var planoId = (await planoResposta.CorpoAsync()).GetProperty("id").GetInt32();
        var criacao = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Preventiva", planoManutencaoId = planoId });
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var osId = (await criacao.CorpoAsync()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await tecnico.PostAsync($"/api/ordens-servico/{osId}/assumir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tecnico.PostAsync($"/api/ordens-servico/{osId}/iniciar", null)).StatusCode);

        var conclusao = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir", new
        {
            diagnostico = "Inspecao sem irregularidades.", servicoRealizado = "Lubrificacao realizada.",
            problemaResolvido = true, dataConclusao
        });
        Assert.Equal(valida ? HttpStatusCode.OK : HttpStatusCode.BadRequest, conclusao.StatusCode);
        if (!valida)
            Assert.Equal("Data de conclusão e periodicidade ultrapassam o limite da próxima execução.", await conclusao.ErroAsync());

        var os = await (await admin.GetAsync($"/api/ordens-servico/{osId}")).CorpoAsync();
        var equipamento = await (await admin.GetAsync($"/api/equipamentos/{equipamentoId}")).CorpoAsync();
        var plano = await (await admin.GetAsync($"/api/planos-manutencao/{planoId}")).CorpoAsync();
        Assert.Equal(valida ? "Concluida" : "EmAtendimento", os.GetProperty("status").GetString());
        Assert.Equal(valida ? "Funcionando" : "EmManutencao", equipamento.GetProperty("status").GetString());
        if (valida)
        {
            Assert.Equal(new DateTime(9999, 12, 31, 23, 59, 59).Ticks,
                plano.GetProperty("proximaExecucao").GetDateTime().Ticks);
            Assert.Equal(new DateTime(9999, 12, 1, 23, 59, 59).Ticks,
                plano.GetProperty("ultimaExecucao").GetDateTime().Ticks);
        }
        else
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, plano.GetProperty("ultimaExecucao").ValueKind);
            Assert.Equal(new DateTime(2027, 1, 1).Ticks, plano.GetProperty("proximaExecucao").GetDateTime().Ticks);
            var manutencao = os.GetProperty("manutencao");
            Assert.Equal(System.Text.Json.JsonValueKind.Null, manutencao.GetProperty("dataConclusao").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null, manutencao.GetProperty("problemaResolvido").ValueKind);
            Assert.Equal("", manutencao.GetProperty("diagnostico").GetString());
            Assert.Equal("", manutencao.GetProperty("servicoRealizado").GetString());
        }
    }

    private static object Dados(int equipamentoId) => new
    {
        equipamentoId, descricao = "Inspecao preventiva revisada", periodicidadeDias = 30,
        proximaExecucao = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static async Task ConcluirAsync(HttpClient tecnico, int osId)
    {
        var resposta = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir", new
        {
            diagnostico = "Inspecao sem irregularidades.", servicoRealizado = "Lubrificacao realizada.",
            problemaResolvido = true
        });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
