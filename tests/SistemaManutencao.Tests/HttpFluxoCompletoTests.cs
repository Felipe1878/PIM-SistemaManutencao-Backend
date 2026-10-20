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

    private async Task ExecutarFluxoCompletoAsync(string sufixo, bool problemaResolvido,
        string statusOcorrenciaFinal, string statusEquipamentoFinal)
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
        var iniciar = await tecnico.PostAsync($"/api/ordens-servico/{osId}/iniciar", null);
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
        var registrarManutencao = await tecnico.PutAsJsonAsync($"/api/ordens-servico/{osId}/manutencao",
            new { diagnostico = "Rolamento danificado confirmado", servicoRealizado = "Rolamento substituido" });
        Assert.Equal(HttpStatusCode.OK, registrarManutencao.StatusCode);
        var concluir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir",
            new { diagnostico = "Rolamento danificado confirmado", servicoRealizado = "Rolamento substituido",
                observacoes = problemaResolvido ? "Equipamento liberado" : "Pendencia de peca", problemaResolvido });
        Assert.Equal(HttpStatusCode.OK, concluir.StatusCode);

        // Asserts finais: estados persistidos conforme o desfecho.
        var osFinal = await (await admin.GetAsync($"/api/ordens-servico/{osId}")).CorpoAsync();
        Assert.Equal("Concluida", osFinal.GetProperty("status").GetString());
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
