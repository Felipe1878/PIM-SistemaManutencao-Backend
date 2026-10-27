using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SistemaManutencao.Tests;

public sealed class E2EFixtureSegurancaTests
{
    [Fact]
    public void CadaFixtureTemNomeExclusivo()
    {
        var campo = typeof(E2ESqlServerFixture).GetField("NomeBancoTemp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static)!;
        var primeiro = (string)campo.GetValue(new E2ESqlServerFixture())!;
        var segundo = (string)campo.GetValue(new E2ESqlServerFixture())!;
        Assert.NotEqual(primeiro, segundo);
        Assert.StartsWith("SistemaManutencaoPIM_E2E_", primeiro);
        Assert.True(Guid.TryParseExact(primeiro["SistemaManutencaoPIM_E2E_".Length..], "N", out _));
    }

    [Fact]
    public async Task CleanupSemInicializacaoERepetidoNaoAcessaBanco()
    {
        var fixture = new E2ESqlServerFixture();
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();
    }
}


/// <summary>
/// E2E (A) — fluxo corretivo COMPLETO contra SQL Server LocalDB real (banco descartável
/// SistemaManutencaoPIM_E2E_&lt;GUID&gt;, migrations + seed aplicados pelo startup da própria API):
/// login Admin → setor → equipamento → operador → técnico → login Operador → ocorrência
/// (equipamento Parado) → Admin abre OS corretiva → Técnico assume → inicia
/// (equipamento EmManutencao) → registra manutenção → conclui problemaResolvido=true
/// → OS "Concluida", ocorrência "Resolvida", equipamento "Funcionando".
/// </summary>
[Collection(E2ESqlServerFixture.NomeCollection)]
[Trait("Categoria", "SqlServer")]
public sealed class E2ECorretivaFluxoCompletoTests
{
    private readonly E2ESqlServerFixture _fixture;
    public E2ECorretivaFluxoCompletoTests(E2ESqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FluxoCorretivoCompleto_FechaCicloNoSqlServer()
    {
        // ---- Admin: cadastros base via API real ----
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Producao E2E Corretiva");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId,
            "Prensa E2E Corretiva", "PAT-E2E-CORR-001");

        await _fixture.RegistrarUsuarioAsync(E2ESqlServerFixture.NomeOperador, E2ESqlServerFixture.EmailOperador, "Operador");
        await _fixture.RegistrarUsuarioAsync(E2ESqlServerFixture.NomeTecnico, E2ESqlServerFixture.EmailTecnico, "Tecnico");
        var usuarios = await E2ESqlServerFixture.GetCorpoAsync(admin, "/api/usuarios");
        var tecnicoId = IdDoUsuarioPorEmail(usuarios, E2ESqlServerFixture.EmailTecnico);

        // ---- Operador: registra ocorrência; equipamento vai a Parado ----
        var operador = await _fixture.OperadorAsync();
        var respostaOcorrencia = await operador.PostAsJsonAsync("/api/ocorrencias",
            new { equipamentoId, descricao = "Prensa nao aciona o pedal apos energizar.", prioridade = "Alta" });
        Assert.Equal(HttpStatusCode.Created, respostaOcorrencia.StatusCode);
        var corpoOcorrencia = await respostaOcorrencia.CorpoAsync();
        var ocorrenciaId = corpoOcorrencia.GetProperty("id").GetInt32();
        Assert.Equal("Aberta", corpoOcorrencia.GetProperty("status").GetString());

        var equipamentoAposOcorrencia = await E2ESqlServerFixture.GetCorpoAsync(
            operador, $"/api/equipamentos/{equipamentoId}");
        Assert.Equal("Parado", equipamentoAposOcorrencia.GetProperty("status").GetString());

        // ---- Admin: abre a OS corretiva para a ocorrência ----
        var respostaOs = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Created, respostaOs.StatusCode);
        var corpoOs = await respostaOs.CorpoAsync();
        var osId = corpoOs.GetProperty("id").GetInt32();
        Assert.Equal("Aberta", corpoOs.GetProperty("status").GetString());

        // ---- Técnico: assume, inicia (equipamento EmManutencao) e registra manutenção ----
        var tecnico = await _fixture.TecnicoAsync();
        var respostaAssumir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/assumir", new { });
        Assert.Equal(HttpStatusCode.OK, respostaAssumir.StatusCode);
        var corpoAssumir = await respostaAssumir.CorpoAsync();
        // AssumirAsync reserva a OS (grava TecnicoId via ExecuteUpdateAsync) SEM mudar o status:
        // a OS permanece "Aberta" até o /iniciar, que a leva a "EmAtendimento"
        // (OrdemServicoService.AssumirAsync:58-66 e IniciarAsync:90).
        Assert.Equal("Aberta", corpoAssumir.GetProperty("status").GetString());
        Assert.Equal(tecnicoId, corpoAssumir.GetProperty("tecnicoId").GetInt32());

        var respostaIniciar = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/iniciar", new { });
        Assert.Equal(HttpStatusCode.OK, respostaIniciar.StatusCode);
        Assert.Equal("EmAtendimento", (await respostaIniciar.CorpoAsync()).GetProperty("status").GetString());

        var equipamentoEmManutencao = await E2ESqlServerFixture.GetCorpoAsync(
            tecnico, $"/api/equipamentos/{equipamentoId}");
        Assert.Equal("EmManutencao", equipamentoEmManutencao.GetProperty("status").GetString());

        var respostaManutencao = await tecnico.PutAsJsonAsync($"/api/ordens-servico/{osId}/manutencao",
            new
            {
                diagnostico = "Solenoide do pedal queimada.",
                servicoRealizado = "Substituicao da solenoide e teste de acionamento."
            });
        Assert.Equal(HttpStatusCode.OK, respostaManutencao.StatusCode);
        Assert.Equal("Solenoide do pedal queimada.",
            (await respostaManutencao.CorpoAsync()).GetProperty("diagnostico").GetString());

        // ---- Técnico: conclui com problema resolvido ----
        var respostaConcluir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir",
            new { problemaResolvido = true });
        Assert.Equal(HttpStatusCode.OK, respostaConcluir.StatusCode);
        var corpoConcluir = await respostaConcluir.CorpoAsync();
        Assert.Equal("Concluida", corpoConcluir.GetProperty("status").GetString());
        Assert.True(corpoConcluir.GetProperty("problemaResolvido").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(corpoConcluir.GetProperty("dataConclusao").GetString()),
            "Conclusão deve registrar dataConclusao em UTC.");

        // ---- Estado final: ocorrência Resolvida, equipamento Funcionando, OS concluída com serviço ----
        var ocorrenciaFinal = await E2ESqlServerFixture.GetCorpoAsync(operador, $"/api/ocorrencias/{ocorrenciaId}");
        Assert.Equal("Resolvida", ocorrenciaFinal.GetProperty("status").GetString());

        var equipamentoFinal = await E2ESqlServerFixture.GetCorpoAsync(operador, $"/api/equipamentos/{equipamentoId}");
        Assert.Equal("Funcionando", equipamentoFinal.GetProperty("status").GetString());

        var osDetalhe = await E2ESqlServerFixture.GetCorpoAsync(admin, $"/api/ordens-servico/{osId}");
        Assert.Equal("Concluida", osDetalhe.GetProperty("status").GetString());
        Assert.Equal("Solenoide do pedal queimada.",
            osDetalhe.GetProperty("manutencao").GetProperty("diagnostico").GetString());
        Assert.Equal("Substituicao da solenoide e teste de acionamento.",
            osDetalhe.GetProperty("manutencao").GetProperty("servicoRealizado").GetString());
    }

    private static int IdDoUsuarioPorEmail(JsonElement usuarios, string email)
    {
        foreach (var usuario in usuarios.EnumerateArray())
            if (string.Equals(usuario.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase))
                return usuario.GetProperty("id").GetInt32();
        Assert.Fail($"Usuário {email} não encontrado em GET /api/usuarios.");
        return 0;
    }
}

/// <summary>
/// E2E (B) — fluxo preventivo contra SQL Server LocalDB real: Admin cria plano → OS preventiva
/// manual → Técnico assume/inicia/registra → conclusão. Dois cenários isolados (equipamentos
/// novos): problemaResolvido=true atualiza UltimaExecucao/ProximaExecucao do plano; e
/// problemaResolvido=false deixa o equipamento Parado e o plano SEM atualização.
/// </summary>
[Collection(E2ESqlServerFixture.NomeCollection)]
[Trait("Categoria", "SqlServer")]
public sealed class E2EPreventivaFluxoTests
{
    private readonly E2ESqlServerFixture _fixture;
    public E2EPreventivaFluxoTests(E2ESqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PreventivaConcluidaComSucesso_AtualizaPlanoEEquipamentoFuncionando()
    {
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Utilidades E2E Preventiva");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId, "Compressor E2E", "PAT-E2E-PREV-001");
        var planoId = await _fixture.CriarPlanoAsync(equipamentoId,
            "Lubrificacao e inspecao semanal", 30, DateTime.UtcNow.AddDays(7));

        var planoInicial = await E2ESqlServerFixture.GetCorpoAsync(admin, $"/api/planos-manutencao/{planoId}");
        Assert.True(planoInicial.GetProperty("ultimaExecucao").ValueKind == JsonValueKind.Null,
            "Plano recém-criado não deveria ter última execução registrada.");

        // ---- Admin: OS preventiva manual para o plano ----
        var respostaOs = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Preventiva", planoManutencaoId = planoId });
        Assert.Equal(HttpStatusCode.Created, respostaOs.StatusCode);
        var osId = (await respostaOs.CorpoAsync()).GetProperty("id").GetInt32();

        // ---- Técnico: assume, inicia (com dataInicio UTC explícita) e registra a manutenção ----
        var tecnico = await _fixture.TecnicoAsync();
        var respostaAssumir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/assumir", new { });
        Assert.Equal(HttpStatusCode.OK, respostaAssumir.StatusCode);

        var respostaIniciar = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/iniciar",
            new { dataInicio = DateTime.UtcNow });
        Assert.Equal(HttpStatusCode.OK, respostaIniciar.StatusCode);

        var respostaManutencao = await tecnico.PutAsJsonAsync($"/api/ordens-servico/{osId}/manutencao",
            new
            {
                diagnostico = "Desgaste leve no mancal do compressor.",
                servicoRealizado = "Lubrificacao completa, aperto do mancal e teste de rotacao."
            });
        Assert.Equal(HttpStatusCode.OK, respostaManutencao.StatusCode);

        // ---- Conclusão com problemaResolvido=true e dataConclusao UTC explícita ----
        var dataConclusao = DateTime.UtcNow.AddMinutes(1);
        var respostaConcluir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir",
            new { problemaResolvido = true, dataConclusao });
        Assert.Equal(HttpStatusCode.OK, respostaConcluir.StatusCode);
        Assert.Equal("Concluida", (await respostaConcluir.CorpoAsync()).GetProperty("status").GetString());

        // ---- Plano: última execução = conclusão; próxima = conclusão + 30 dias (periodicidade) ----
        var planoFinal = await E2ESqlServerFixture.GetCorpoAsync(admin, $"/api/planos-manutencao/{planoId}");
        var ultima = E2ESqlServerFixture.ParseInstante(planoFinal.GetProperty("ultimaExecucao").GetString());
        var proxima = E2ESqlServerFixture.ParseInstante(planoFinal.GetProperty("proximaExecucao").GetString());
        Assert.InRange((ultima - dataConclusao).TotalSeconds, -1, 1);
        Assert.InRange((proxima - dataConclusao.AddDays(30)).TotalSeconds, -1, 1);

        var equipamentoFinal = await E2ESqlServerFixture.GetCorpoAsync(tecnico, $"/api/equipamentos/{equipamentoId}");
        Assert.Equal("Funcionando", equipamentoFinal.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PreventivaConcluidaSemResolucao_EquipamentoParadoEPlanoNaoAtualizado()
    {
        // PENDÊNCIA D2 (fixa o comportamento ATUAL da API — sem alterá-la):
        // OrdemServicoService.ConcluirAsync só atualiza UltimaExecucao/ProximaExecucao do plano
        // quando problemaResolvido=true. Com false, a preventiva conclui, o equipamento fica
        // Parado, mas o plano permanece sem registro de execução (ProximaExecucao antiga
        // preservada, UltimaExecucao nula). Este teste documenta essa lacuna para que a decisão
        // futura sobre atualizar (ou não) o plano neste caso seja uma mudança consciente.
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Expedicao E2E Preventiva");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId, "Esteira E2E", "PAT-E2E-PREV-002");
        var planoId = await _fixture.CriarPlanoAsync(equipamentoId,
            "Revisao eletrica trimestral", 90, DateTime.UtcNow.AddDays(15));

        var planoInicial = await E2ESqlServerFixture.GetCorpoAsync(admin, $"/api/planos-manutencao/{planoId}");
        // Compara o INSTANTE (valor de negócio), não o texto JSON bruto: o teste deve falhar por
        // mudança de comportamento, nunca por mudança no formato de serialização do controller.
        var proximaOriginal = E2ESqlServerFixture.ParseInstante(planoInicial.GetProperty("proximaExecucao").GetString());

        var respostaOs = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Preventiva", planoManutencaoId = planoId });
        Assert.Equal(HttpStatusCode.Created, respostaOs.StatusCode);
        var osId = (await respostaOs.CorpoAsync()).GetProperty("id").GetInt32();

        var tecnico = await _fixture.TecnicoAsync();
        var respostaAssumir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/assumir", new { });
        Assert.Equal(HttpStatusCode.OK, respostaAssumir.StatusCode);
        var respostaIniciar = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/iniciar", new { });
        Assert.Equal(HttpStatusCode.OK, respostaIniciar.StatusCode);
        var respostaManutencao = await tecnico.PutAsJsonAsync($"/api/ordens-servico/{osId}/manutencao",
            new
            {
                diagnostico = "Inspecao eletrica incompleta por falta de peca.",
                servicoRealizado = "Inspecao parcial e sinalizacao de pendencia."
            });
        Assert.Equal(HttpStatusCode.OK, respostaManutencao.StatusCode);

        var respostaConcluir = await tecnico.PostAsJsonAsync($"/api/ordens-servico/{osId}/concluir",
            new { problemaResolvido = false });
        Assert.Equal(HttpStatusCode.OK, respostaConcluir.StatusCode);
        Assert.Equal("Concluida", (await respostaConcluir.CorpoAsync()).GetProperty("status").GetString());

        // Equipamento volta a Parado (pendência física permanece).
        var equipamentoFinal = await E2ESqlServerFixture.GetCorpoAsync(admin, $"/api/equipamentos/{equipamentoId}");
        Assert.Equal("Parado", equipamentoFinal.GetProperty("status").GetString());

        // Plano NÃO atualizado (D2): sem última execução e próxima execução intacta.
        var planoFinal = await E2ESqlServerFixture.GetCorpoAsync(admin, $"/api/planos-manutencao/{planoId}");
        Assert.True(planoFinal.GetProperty("ultimaExecucao").ValueKind == JsonValueKind.Null,
            "D2: com problemaResolvido=false o plano não deve registrar última execução (comportamento atual).");
        var proximaFinal = E2ESqlServerFixture.ParseInstante(planoFinal.GetProperty("proximaExecucao").GetString());
        Assert.Equal(proximaOriginal.ToUniversalTime(), proximaFinal.ToUniversalTime());
    }
}

/// <summary>
/// E2E (C) — integridade de dados e autorização contra SQL Server LocalDB real:
/// patrimônio duplicado (409), segunda ocorrência ativa no mesmo equipamento (409),
/// segunda OS para a mesma ocorrência (409), corretiva com planoManutencaoId (400),
/// Operador criando setor (403), Técnico criando OS (403) e GET /api/usuarios sem JWT (401).
/// A geração automática de OS preventiva está fora do escopo do MVP e NÃO é testada.
/// </summary>
[Collection(E2ESqlServerFixture.NomeCollection)]
[Trait("Categoria", "SqlServer")]
public sealed class E2EIntegridadeAutorizacaoTests
{
    private readonly E2ESqlServerFixture _fixture;
    public E2EIntegridadeAutorizacaoTests(E2ESqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PatrimonioDuplicado_Retorna409()
    {
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Integridade E2E Patrimonio");

        var primeira = await admin.PostAsJsonAsync("/api/equipamentos",
            new { nome = "Torno E2E Integridade", patrimonio = "PAT-E2E-DUP-001", setorId });
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        var segunda = await admin.PostAsJsonAsync("/api/equipamentos",
            new { nome = "Torno E2E Integridade 2", patrimonio = "PAT-E2E-DUP-001", setorId });
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await segunda.ErroAsync()),
            "409 de patrimônio duplicado deve usar o envelope {erro}.");
    }

    [Fact]
    public async Task SegundaOcorrenciaAtivaMesmoEquipamento_Retorna409()
    {
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Integridade E2E Ocorrencia");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId, "Fresadora E2E", "PAT-E2E-OCOR-DUP");

        await _fixture.CriarOcorrenciaAsync(equipamentoId, "Primeira ocorrencia ativa da fresadora.");

        var operador = await _fixture.OperadorAsync();
        var segunda = await operador.PostAsJsonAsync("/api/ocorrencias",
            new { equipamentoId, descricao = "Segunda ocorrencia para o mesmo equipamento.", prioridade = "Media" });
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await segunda.ErroAsync()),
            "409 de ocorrência ativa duplicada deve usar o envelope {erro}.");
    }

    [Fact]
    public async Task SegundaOsParaMesmaOcorrencia_Retorna409()
    {
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Integridade E2E OS");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId, "Robo E2E", "PAT-E2E-OS-DUP");
        var ocorrenciaId = await _fixture.CriarOcorrenciaAsync(equipamentoId, "Ocorrencia que ja tera OS.");

        var primeira = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);

        var segunda = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await segunda.ErroAsync()),
            "409 de OS duplicada para a ocorrência deve usar o envelope {erro}.");
    }

    [Fact]
    public async Task CorretivaComPlanoManutencaoId_Retorna400()
    {
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Integridade E2E Tipo");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId, "Serra E2E", "PAT-E2E-TIPO-400");
        var planoId = await _fixture.CriarPlanoAsync(equipamentoId,
            "Plano para validar DTO corretivo", 30, DateTime.UtcNow.AddDays(10));

        var resposta = await admin.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", planoManutencaoId = planoId });
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await resposta.ErroAsync()),
            "400 de DTO corretivo malformado deve usar o envelope {erro}.");
    }

    [Fact]
    public async Task OperadorNaoCriaSetor_Retorna403()
    {
        var operador = await _fixture.OperadorAsync();
        var resposta = await operador.PostAsJsonAsync("/api/setores",
            new { nome = "Setor Proibido Operador E2E" });
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await resposta.ErroAsync()),
            "403 deve usar o envelope {erro}.");
    }

    [Fact]
    public async Task TecnicoNaoCriaOrdemServico_Retorna403()
    {
        var admin = await _fixture.AdminAsync();
        var setorId = await _fixture.CriarSetorAsync("Integridade E2E Autorizacao");
        var equipamentoId = await _fixture.CriarEquipamentoAsync(setorId, "Guincho E2E", "PAT-E2E-AUT-403");
        var ocorrenciaId = await _fixture.CriarOcorrenciaAsync(equipamentoId, "Ocorrencia para tentativa de tecnico.");

        var tecnico = await _fixture.TecnicoAsync();
        var resposta = await tecnico.PostAsJsonAsync("/api/ordens-servico",
            new { tipoManutencao = "Corretiva", ocorrenciaId });
        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await resposta.ErroAsync()),
            "403 deve usar o envelope {erro}.");
    }

    [Fact]
    public async Task GetUsuariosSemJwt_Retorna401ComEnvelopeEDesafio()
    {
        var anonimo = _fixture.ClienteAnonimo();
        var resposta = await anonimo.GetAsync("/api/usuarios");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await resposta.ErroAsync()),
            "401 do middleware JWT deve usar o envelope {erro}.");
        Assert.True(resposta.Headers.Contains("WWW-Authenticate"),
            "401 deve vir acompanhado do desafio WWW-Authenticate: Bearer.");
    }
}
