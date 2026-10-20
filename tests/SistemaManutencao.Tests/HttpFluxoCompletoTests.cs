using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Gate 3 — Fluxo completo de OS corretiva sobre HTTP com os dois desfechos:
/// problemaResolvido=true (OS Concluida, ocorrência Resolvida, equipamento Funcionando)
/// e problemaResolvido=false (OS Concluida, ocorrência Aberta, equipamento Parado).
/// Percorre: login real → ocorrência (operador) → OS (admin) → assumir/iniciar/
/// manutenção/concluir (técnico), com asserts intermediários e finais de estado.
/// </summary>
[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpFluxoCompletoTests
{
    private readonly ApiHttpFixture _api;

    public HttpFluxoCompletoTests(ApiHttpFixture api) => _api = api;

    [Fact]
    public async Task Fluxo_com_problema_resolvido_true_encerra_OS_ocorrencia_e_libera_equipamento()
        => await ExecutarFluxoCompletoAsync("A", problemaResolvido: true,
            statusOcorrenciaFinal: "Resolvida", statusEquipamentoFinal: "Funcionando");

    [Fact]
    public async Task Fluxo_com_problema_resolvido_false_conclui_OS_mantem_ocorrencia_aberta_e_equipamento_parado()
        => await ExecutarFluxoCompletoAsync("B", problemaResolvido: false,
            statusOcorrenciaFinal: "Aberta", statusEquipamentoFinal: "Parado");

    [Fact]
    public async Task Fluxo_aceita_datas_UTC_informadas_e_conclusao_sem_repetir_textos_preserva_observacoes()
        => await ExecutarFluxoCompletoAsync("datas-informadas", problemaResolvido: true,
            statusOcorrenciaFinal: "Resolvida", statusEquipamentoFinal: "Funcionando",
            informarDatas: true, concluirSemRepetirTextos: true);

    [Fact]
    public async Task Fluxo_sem_textos_previamente_registrados_aceita_textos_na_conclusao()
        => await ExecutarFluxoCompletoAsync("textos-na-conclusao", problemaResolvido: true,
            statusOcorrenciaFinal: "Resolvida", statusEquipamentoFinal: "Funcionando",
            registrarAntesDeConcluir: false);

    [Fact]
    public async Task Conclusao_rejeita_data_anterior_ao_inicio()
    {
        var admin = await _api.AdminAsync();
        var tecnico = await _api.TecnicoAsync();
        var (setorId, equipamentoId, ocorrenciaId) = await _api.CriarCenarioOcorrenciaAsync(
            "cronologia", "Falha para validar cronologia das datas.");
        _ = setorId;
        var criarOs = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        var osId = (await criarOs.CorpoAsync()).GetProperty("id").GetInt32();
        await tecnico.PostAsync($"/api/ordens-servico/{osId}/assumir", null);
        var dataComFusoLocal = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/iniciar",
            new { dataInicio = "2026-10-20T09:00:00-03:00" });
        Assert.Equal(HttpStatusCode.BadRequest, dataComFusoLocal.StatusCode);
        await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/iniciar",
            new { dataInicio = "2026-10-20T12:00:00Z" });

        var concluir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir", new
        {
            diagnostico = "Falha elétrica confirmada.", servicoRealizado = "Conexão revisada.",
            dataConclusao = "2026-10-20T11:59:59Z", problemaResolvido = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, concluir.StatusCode);
        Assert.Equal("Data de conclusão não pode ser anterior à data de início.", await concluir.ErroAsync());
        var os = await (await admin.GetAsync($"/api/ordens-servico/{osId}")).CorpoAsync();
        Assert.Equal("EmAtendimento", os.GetProperty("status").GetString());
        Assert.Equal("EmManutencao", await StatusEquipamentoAsync(admin, equipamentoId));
    }

    private async Task ExecutarFluxoCompletoAsync(string sufixo, bool problemaResolvido,
        string statusOcorrenciaFinal, string statusEquipamentoFinal,
        bool informarDatas = false, bool concluirSemRepetirTextos = false, bool registrarAntesDeConcluir = true)
    {
        var admin = await _api.AdminAsync();
        var operador = await _api.OperadorAsync();
        var tecnico = await _api.TecnicoAsync();

        // Admin prepara setor e equipamento Funcionando.
        var setorId = await _api.CriarSetorAsync($"Setor Fluxo {sufixo}");
        var equipamentoId = await _api.CriarEquipamentoAsync(setorId, $"Equipamento Fluxo {sufixo}", $"PATRIMONIO-{sufixo}");
        Assert.Equal("Funcionando", await StatusEquipamentoAsync(admin, equipamentoId));

        // Operador registra a ocorrência → equipamento vai para Parado.
        var criacaoOcorrencia = await operador.PostAsJsonAsync("/api/ocorrencias",
            new { equipamentoId, descricao = $"Ocorrencia do fluxo {sufixo}: falha no funcionamento", prioridade = "Alta" });
        Assert.Equal(HttpStatusCode.Created, criacaoOcorrencia.StatusCode);
        var ocorrenciaId = (await criacaoOcorrencia.CorpoAsync()).GetProperty("id").GetInt32();
        Assert.Equal("Parado", await StatusEquipamentoAsync(admin, equipamentoId));

        // A ocorrência aparece em /api/ocorrencias/minhas do operador.
        var minhas = await (await operador.GetAsync("/api/ocorrencias/minhas")).CorpoAsync();
        Assert.Contains(minhas.EnumerateArray(), o => o.GetProperty("id").GetInt32() == ocorrenciaId);

        // Admin abre a OS corretiva vinculada à ocorrência.
        var criacaoOs = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Created, criacaoOs.StatusCode);
        var osId = (await criacaoOs.CorpoAsync()).GetProperty("id").GetInt32();

        // Técnico assume e inicia a OS.
        var assumir = await tecnico.PostAsync($"/api/ordens-servico/{osId}/assumir", null);
        Assert.Equal(HttpStatusCode.OK, assumir.StatusCode);
        var iniciar = informarDatas
            ? await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/iniciar",
                new { dataInicio = "2026-10-20T10:00:00Z" })
            : await tecnico.PostAsync($"/api/ordens-servico/{osId}/iniciar", null);
        Assert.Equal(HttpStatusCode.OK, iniciar.StatusCode);

        // Asserts intermediários: OS EmAtendimento com manutenção datada, equipamento
        // EmManutencao e ocorrência EmAtendimento.
        var osEmAtendimento = await (await admin.GetAsync($"/api/ordens-servico/{osId}")).CorpoAsync();
        Assert.Equal("EmAtendimento", osEmAtendimento.GetProperty("status").GetString());
        var manutencao = osEmAtendimento.GetProperty("manutencao");
        Assert.Equal(JsonValueKind.Object, manutencao.ValueKind);
        Assert.True(manutencao.GetProperty("dataInicio").GetDateTime() != default, "manutencao deve ter dataInicio.");
        Assert.Equal("EmManutencao", await StatusEquipamentoAsync(admin, equipamentoId));
        var ocorrenciaEmAtendimento = await (await admin.GetAsync($"/api/ocorrencias/{ocorrenciaId}")).CorpoAsync();
        Assert.Equal("EmAtendimento", ocorrenciaEmAtendimento.GetProperty("status").GetString());

        // Técnico registra a manutenção e conclui a OS.
        if (registrarAntesDeConcluir)
        {
            var registrarManutencao = await tecnico.PutAsJsonAsync($"/api/ordens-servico/{osId}/manutencao",
                new { diagnostico = "Rolamento danificado confirmado", servicoRealizado = "Rolamento substituido",
                    observacoes = concluirSemRepetirTextos ? "Observação registrada antes da conclusão" : null });
            Assert.Equal(HttpStatusCode.OK, registrarManutencao.StatusCode);
        }
        var corpoConclusao = new Dictionary<string, object?> { ["problemaResolvido"] = problemaResolvido };
        if (!concluirSemRepetirTextos)
        {
            corpoConclusao["diagnostico"] = "Rolamento danificado confirmado";
            corpoConclusao["servicoRealizado"] = "Rolamento substituido";
            corpoConclusao["observacoes"] = problemaResolvido ? "Equipamento liberado" : "Pendencia de peca";
        }
        if (informarDatas) corpoConclusao["dataConclusao"] = "2026-10-20T11:00:00Z";
        var concluir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir", corpoConclusao);
        Assert.Equal(HttpStatusCode.OK, concluir.StatusCode);

        // Asserts finais: estados persistidos conforme o desfecho.
        var osFinal = await (await admin.GetAsync($"/api/ordens-servico/{osId}")).CorpoAsync();
        Assert.Equal("Concluida", osFinal.GetProperty("status").GetString());
        var manutencaoFinal = osFinal.GetProperty("manutencao");
        Assert.Equal("Rolamento danificado confirmado", manutencaoFinal.GetProperty("diagnostico").GetString());
        Assert.Equal("Rolamento substituido", manutencaoFinal.GetProperty("servicoRealizado").GetString());
        Assert.NotEqual(default, manutencaoFinal.GetProperty("dataInicio").GetDateTime());
        Assert.NotEqual(default, manutencaoFinal.GetProperty("dataConclusao").GetDateTime());
        Assert.Equal(DateTimeKind.Utc, manutencaoFinal.GetProperty("dataInicio").GetDateTime().Kind);
        Assert.Equal(DateTimeKind.Utc, manutencaoFinal.GetProperty("dataConclusao").GetDateTime().Kind);
        if (concluirSemRepetirTextos)
            Assert.Equal("Observação registrada antes da conclusão", manutencaoFinal.GetProperty("observacoes").GetString());
        if (informarDatas)
        {
            Assert.Equal(DateTimeOffset.Parse("2026-10-20T10:00:00Z").UtcTicks,
                manutencaoFinal.GetProperty("dataInicio").GetDateTime().Ticks);
            Assert.Equal(DateTimeOffset.Parse("2026-10-20T11:00:00Z").UtcTicks,
                manutencaoFinal.GetProperty("dataConclusao").GetDateTime().Ticks);
            Assert.Equal(DateTimeKind.Utc, manutencaoFinal.GetProperty("dataInicio").GetDateTime().Kind);
        }
        var ocorrenciaFinal = await (await admin.GetAsync($"/api/ocorrencias/{ocorrenciaId}")).CorpoAsync();
        Assert.Equal(statusOcorrenciaFinal, ocorrenciaFinal.GetProperty("status").GetString());
        Assert.Equal(statusEquipamentoFinal, await StatusEquipamentoAsync(admin, equipamentoId));
    }

    private static async Task<string> StatusEquipamentoAsync(HttpClient cliente, int id)
    {
        var resposta = await cliente.GetAsync($"/api/equipamentos/{id}");
        resposta.EnsureSuccessStatusCode();
        return (await resposta.CorpoAsync()).GetProperty("status").GetString()!;
    }
}
