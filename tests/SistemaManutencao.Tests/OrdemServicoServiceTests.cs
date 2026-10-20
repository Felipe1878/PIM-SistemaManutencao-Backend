using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;
using SistemaManutencao.API.Services;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Testes de caracterização do OrdemServicoService (comportamento ATUAL).
/// Cada operação roda em um AppDbContext novo sobre a mesma conexão SQLite
/// (ver BancoTeste); leituras de verificação usam contexto novo + AsNoTracking.
/// </summary>
public class OrdemServicoServiceTests
{
    // ---------- Helpers de fluxo ----------

    private static Task<Ocorrencia> CriarOcorrenciaAsync(BancoTeste banco)
        => banco.OpAsync(db => new OcorrenciaService(db).RegistrarAsync(
            new CriarOcorrenciaDto { EquipamentoId = 1, Descricao = "Falha no motor da prensa.",
                Prioridade = PrioridadeOcorrencia.Alta }, 1));

    private static Task<OrdemServico> CriarOsCorretivaAsync(BancoTeste banco, int ocorrenciaId)
        => banco.OpAsync(db => new OrdemServicoService(db).CriarAsync(
            new CriarOrdemServicoDto { TipoManutencao = TipoManutencao.Corretiva, OcorrenciaId = ocorrenciaId }));

    private static Task<OrdemServico> CriarOsPreventivaAsync(BancoTeste banco, int planoId)
        => banco.OpAsync(db => new OrdemServicoService(db).CriarAsync(
            new CriarOrdemServicoDto { TipoManutencao = TipoManutencao.Preventiva, PlanoManutencaoId = planoId }));

    private static Task AssumirAsync(BancoTeste banco, int osId, int tecnicoId)
        => banco.OpAsync(db => new OrdemServicoService(db).AssumirAsync(osId, tecnicoId));

    private static Task IniciarAsync(BancoTeste banco, int osId, int tecnicoId)
        => banco.OpAsync(db => new OrdemServicoService(db).IniciarAsync(osId, tecnicoId));

    private static Task ConcluirAsync(BancoTeste banco, int osId, bool resolvido)
        => banco.OpAsync(db => new OrdemServicoService(db).ConcluirAsync(osId, 2, ConcluirDto(resolvido)));

    /// <summary>Fluxo completo corretiva: registrar → criar OS → assumir (tecnico 2) → iniciar (tecnico 2).</summary>
    private static async Task<(OrdemServico Os, Ocorrencia Ocorrencia)> OsEmAtendimentoAsync(BancoTeste banco)
    {
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await AssumirAsync(banco, os.Id, 2);
        await IniciarAsync(banco, os.Id, 2);
        return (os, ocorrencia);
    }

    private static ConcluirOrdemServicoDto ConcluirDto(bool resolvido) => new()
    {
        Diagnostico = "Motor com falha",
        ServicoRealizado = "Verificacao de conexoes",
        Observacoes = "Acompanhar por uma semana",
        ProblemaResolvido = resolvido
    };

    private static Task<int> SemearTecnicoExtraAsync(BancoTeste banco)
        => banco.OpAsync(async db =>
        {
            var u = new Usuario { Nome = "Tecnico Dois", Email = "tecnico.dois@pim.local", SenhaHash = "teste",
                Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Tecnico };
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            return u.Id;
        });

    private static Task<int> SemearEquipamentoAsync(BancoTeste banco, StatusEquipamento status, bool ativo = true)
        => banco.OpAsync(async db =>
        {
            var e = new Equipamento { Nome = "Torno", Patrimonio = "T01", SetorId = 1, Status = status, Ativo = ativo };
            db.Equipamentos.Add(e);
            await db.SaveChangesAsync();
            return e.Id;
        });

    private static Task<int> SemearPlanoAsync(BancoTeste banco, int equipamentoId, bool ativo = true)
        => banco.OpAsync(async db =>
        {
            var p = new PlanoManutencao { EquipamentoId = equipamentoId, Descricao = "Plano de lubrificacao trimestral",
                PeriodicidadeDias = 30, ProximaExecucao = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Ativo = ativo };
            db.PlanosManutencao.Add(p);
            await db.SaveChangesAsync();
            return p.Id;
        });

    // ---------- Fluxo completo e R10 persistido (2.5) ----------

    [Theory]
    [InlineData(true, StatusOcorrencia.Resolvida, StatusEquipamento.Funcionando)]
    [InlineData(false, StatusOcorrencia.Aberta, StatusEquipamento.Parado)]
    public async Task ConclusaoDeOSRespeitaResultado(bool resolvido, StatusOcorrencia statusOcorrencia,
        StatusEquipamento statusEquipamento)
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await AssumirAsync(banco, os.Id, 2);
        await IniciarAsync(banco, os.Id, 2);
        await ConcluirAsync(banco, os.Id, resolvido);

        var osFinal = await banco.OpAsync(db => db.OrdensServicos.AsNoTracking().SingleAsync(x => x.Id == os.Id));
        var ocorrenciaFinal = await banco.OpAsync(db => db.Ocorrencias.AsNoTracking().SingleAsync(x => x.Id == ocorrencia.Id));
        var equipamentoFinal = await banco.OpAsync(db => db.Equipamentos.AsNoTracking().SingleAsync(x => x.Id == 1));
        var manutencao = await banco.OpAsync(db => db.Manutencoes.AsNoTracking().SingleAsync(x => x.OrdemServicoId == os.Id));

        Assert.Equal(StatusOrdemServico.Concluida, osFinal.Status);
        Assert.Equal(statusOcorrencia, ocorrenciaFinal.Status);
        Assert.Equal(statusEquipamento, equipamentoFinal.Status);
        // R10: manutenção persistida (lida de contexto novo, fora do tracker).
        Assert.Equal("Motor com falha", manutencao.Diagnostico);
        Assert.Equal("Verificacao de conexoes", manutencao.ServicoRealizado);
        Assert.Equal("Acompanhar por uma semana", manutencao.Observacoes);
        Assert.NotEqual(default, manutencao.DataInicio);
        Assert.NotNull(manutencao.DataConclusao);
        Assert.Equal(resolvido, manutencao.ProblemaResolvido);
    }

    // ---------- Prova do 403 legítimo (elimina falso positivo, Tarefa 1.3) ----------

    [Fact]
    public async Task TecnicoDiferenteNaoPodeIniciarOS()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await AssumirAsync(banco, os.Id, 2);

        // O ExecuteUpdateAsync de Assumir precisa ter persistido TecnicoId=2 no banco.
        // Lido de contexto NOVO: sem isso o 403 abaixo poderia vir de registro stale.
        var tecnicoPersistido = await banco.OpAsync(db => db.OrdensServicos.AsNoTracking()
            .Where(x => x.Id == os.Id).Select(x => x.TecnicoId).SingleAsync());
        Assert.Equal((int?)2, tecnicoPersistido);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => IniciarAsync(banco, os.Id, 1));
        Assert.Equal(403, erro.StatusHttp);
        Assert.Equal("Somente o técnico responsável pode iniciar esta OS.", erro.Message);

        // Motivo CERTO: o dono (tecnico 2, que está no banco) consegue iniciar.
        // Se o 403 acima fosse causado por valor stale, esta chamada também falharia.
        var osIniciada = await banco.OpAsync(db => new OrdemServicoService(db).IniciarAsync(os.Id, 2));
        Assert.Equal(StatusOrdemServico.EmAtendimento, osIniciada.Status);
    }

    // ---------- Guardas de Assumir e estado (2.1) ----------

    [Fact]
    public async Task SegundoTecnicoNaoAssumeOSJaAssumida()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await AssumirAsync(banco, os.Id, 2);
        var tecnicoId = await SemearTecnicoExtraAsync(banco);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => AssumirAsync(banco, os.Id, tecnicoId));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("OS inexistente, ocupada ou indisponível.", erro.Message);
    }

    [Fact]
    public async Task AssumirOSInexistenteLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => AssumirAsync(banco, 999, 2));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("OS inexistente, ocupada ou indisponível.", erro.Message);
    }

    [Fact]
    public async Task AssumirOSCanceladaLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await banco.OpAsync(db => new OrdemServicoService(db).CancelarAsync(os.Id));

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => AssumirAsync(banco, os.Id, 2));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("OS inexistente, ocupada ou indisponível.", erro.Message);
    }

    [Fact]
    public async Task IniciarOSJaIniciadaLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var (os, _) = await OsEmAtendimentoAsync(banco);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => IniciarAsync(banco, os.Id, 2));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("OS não está aberta ou já foi iniciada.", erro.Message);
    }

    [Fact]
    public async Task ConcluirSemIniciarLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await AssumirAsync(banco, os.Id, 2);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => ConcluirAsync(banco, os.Id, true));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("A OS precisa estar Em Atendimento para ser concluída.", erro.Message);
    }

    [Fact]
    public async Task ConcluirSemProblemaResolvidoLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var (os, _) = await OsEmAtendimentoAsync(banco);
        var dto = ConcluirDto(true);
        dto.ProblemaResolvido = null;

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(
            () => banco.OpAsync(db => new OrdemServicoService(db).ConcluirAsync(os.Id, 2, dto)));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("Informe problemaResolvido.", erro.Message);
    }

    [Fact]
    public async Task CancelarOSEmAtendimentoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var (os, _) = await OsEmAtendimentoAsync(banco);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(
            () => banco.OpAsync(db => new OrdemServicoService(db).CancelarAsync(os.Id)));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Apenas OS aberta pode ser cancelada.", erro.Message);
    }

    [Fact]
    public async Task CancelarOSConcluidaLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var (os, _) = await OsEmAtendimentoAsync(banco);
        await ConcluirAsync(banco, os.Id, true);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(
            () => banco.OpAsync(db => new OrdemServicoService(db).CancelarAsync(os.Id)));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Apenas OS aberta pode ser cancelada.", erro.Message);
    }

    [Fact]
    public async Task CancelarOSMantemOcorrenciaAbertaED1BloqueiaNovaOS()
    {
        // Comportamento atual: cancelar não devolve a ocorrência — ela segue Aberta com o
        // equipamento Parado, e a relação 1:1 (R5) impede nova OS para ela (deadlock D1 pendente).
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await banco.OpAsync(db => new OrdemServicoService(db).CancelarAsync(os.Id));

        var osFinal = await banco.OpAsync(db => db.OrdensServicos.AsNoTracking().SingleAsync(x => x.Id == os.Id));
        var ocorrenciaFinal = await banco.OpAsync(db => db.Ocorrencias.AsNoTracking().SingleAsync(x => x.Id == ocorrencia.Id));
        var equipamentoFinal = await banco.OpAsync(db => db.Equipamentos.AsNoTracking().SingleAsync(x => x.Id == 1));
        Assert.Equal(StatusOrdemServico.Cancelada, osFinal.Status);
        Assert.Equal(StatusOcorrencia.Aberta, ocorrenciaFinal.Status);
        Assert.Equal(StatusEquipamento.Parado, equipamentoFinal.Status);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsCorretivaAsync(banco, ocorrencia.Id));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Ocorrência já possui OS (relação 1:1).", erro.Message);
    }

    // ---------- R5: 1:1 ocorrência → OS (2.2) ----------

    [Fact]
    public async Task SegundaOSParaOcorrenciaComPrimeiraAbertaLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        await CriarOsCorretivaAsync(banco, ocorrencia.Id);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsCorretivaAsync(banco, ocorrencia.Id));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Ocorrência já possui OS (relação 1:1).", erro.Message);
    }

    [Fact]
    public async Task SegundaOSAposConcluirSemResolucaoLanca409()
    {
        // D1 pendente: concluída SEM resolução, a ocorrência volta a Aberta (equipamento Parado),
        // mas a relação 1:1 atual impede nova OS — deadlock registrado como comportamento ATUAL (409).
        await using var banco = await BancoTeste.CriarAsync();
        var (os, ocorrencia) = await OsEmAtendimentoAsync(banco);
        await ConcluirAsync(banco, os.Id, false);

        var ocorrenciaFinal = await banco.OpAsync(db => db.Ocorrencias.AsNoTracking().SingleAsync(x => x.Id == ocorrencia.Id));
        var equipamentoFinal = await banco.OpAsync(db => db.Equipamentos.AsNoTracking().SingleAsync(x => x.Id == 1));
        Assert.Equal(StatusOcorrencia.Aberta, ocorrenciaFinal.Status);
        Assert.Equal(StatusEquipamento.Parado, equipamentoFinal.Status);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsCorretivaAsync(banco, ocorrencia.Id));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Ocorrência já possui OS (relação 1:1).", erro.Message);
    }

    // ---------- Guardas do DTO de criação ----------

    [Fact]
    public async Task TipoManutencaoForaDoEnumLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).CriarAsync(new CriarOrdemServicoDto { TipoManutencao = (TipoManutencao)99 })));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("Tipo de manutenção inválido.", erro.Message);
    }

    [Fact]
    public async Task CorretivaComPlanoManutencaoIdLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).CriarAsync(new CriarOrdemServicoDto
            { TipoManutencao = TipoManutencao.Corretiva, PlanoManutencaoId = planoId })));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("OS corretiva exige ocorrenciaId e não aceita planoManutencaoId.", erro.Message);
    }

    [Fact]
    public async Task CorretivaSemOcorrenciaLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).CriarAsync(new CriarOrdemServicoDto { TipoManutencao = TipoManutencao.Corretiva })));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("OS corretiva exige ocorrenciaId e não aceita planoManutencaoId.", erro.Message);
    }

    [Fact]
    public async Task PreventivaComOcorrenciaIdLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).CriarAsync(new CriarOrdemServicoDto
            { TipoManutencao = TipoManutencao.Preventiva, OcorrenciaId = ocorrencia.Id })));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("OS preventiva exige planoManutencaoId e não aceita ocorrenciaId.", erro.Message);
    }

    [Fact]
    public async Task PreventivaSemPlanoLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).CriarAsync(new CriarOrdemServicoDto { TipoManutencao = TipoManutencao.Preventiva })));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("OS preventiva exige planoManutencaoId e não aceita ocorrenciaId.", erro.Message);
    }

    // ---------- Preventiva: pré-condições (2.4) ----------

    [Fact]
    public async Task PreventivaComPlanoInativoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando);
        var planoId = await SemearPlanoAsync(banco, equipamentoId, ativo: false);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsPreventivaAsync(banco, planoId));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Plano ou equipamento inativo.", erro.Message);
    }

    [Fact]
    public async Task PreventivaComEquipamentoInativoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando, ativo: false);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsPreventivaAsync(banco, planoId));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Plano ou equipamento inativo.", erro.Message);
    }

    [Fact]
    public async Task PreventivaComEquipamentoParadoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Parado);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsPreventivaAsync(banco, planoId));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Preventiva requer equipamento Funcionando e sem ocorrência ativa.", erro.Message);
    }

    [Fact]
    public async Task PreventivaComOcorrenciaAtivaLanca409()
    {
        // Ocorrência ativa semeada direto mantendo o equipamento Funcionando: isola o
        // predicado "sem ocorrência ativa" (segunda metade do OU) da checagem de status.
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);
        await banco.OpAsync(db =>
        {
            db.Ocorrencias.Add(new Ocorrencia { EquipamentoId = equipamentoId, UsuarioId = 1,
                Descricao = "Ruido estranho no eixo.", Prioridade = PrioridadeOcorrencia.Media,
                Status = StatusOcorrencia.Aberta });
            return db.SaveChangesAsync();
        });

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsPreventivaAsync(banco, planoId));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Preventiva requer equipamento Funcionando e sem ocorrência ativa.", erro.Message);
    }

    [Fact]
    public async Task SegundaPreventivaAbertaParaOMesmoPlanoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);
        await CriarOsPreventivaAsync(banco, planoId);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => CriarOsPreventivaAsync(banco, planoId));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Já existe OS preventiva não finalizada para esse plano.", erro.Message);
    }

    [Fact]
    public async Task PreventivaAposConcluirPermiteNovaAbertura()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);
        var os = await CriarOsPreventivaAsync(banco, planoId);
        await AssumirAsync(banco, os.Id, 2);
        await IniciarAsync(banco, os.Id, 2);
        await ConcluirAsync(banco, os.Id, true);

        var nova = await CriarOsPreventivaAsync(banco, planoId);
        Assert.Equal(StatusOrdemServico.Aberta, nova.Status);
    }

    [Fact]
    public async Task PreventivaConcluidaAtualizaDatasDoPlano()
    {
        // Comportamento atual (OrdemServicoService.ConcluirAsync): com problemaResolvido=true,
        // UltimaExecucao = data da conclusão e ProximaExecucao = conclusão + periodicidade.
        await using var banco = await BancoTeste.CriarAsync();
        var equipamentoId = await SemearEquipamentoAsync(banco, StatusEquipamento.Funcionando);
        var planoId = await SemearPlanoAsync(banco, equipamentoId);
        var os = await CriarOsPreventivaAsync(banco, planoId);
        await AssumirAsync(banco, os.Id, 2);
        await IniciarAsync(banco, os.Id, 2);
        await ConcluirAsync(banco, os.Id, true);

        var planoFinal = await banco.OpAsync(db => db.PlanosManutencao.AsNoTracking().SingleAsync(x => x.Id == planoId));
        var osFinal = await banco.OpAsync(db => db.OrdensServicos.AsNoTracking().SingleAsync(x => x.Id == os.Id));
        var equipamentoFinal = await banco.OpAsync(db => db.Equipamentos.AsNoTracking().SingleAsync(x => x.Id == equipamentoId));

        Assert.Equal(StatusOrdemServico.Concluida, osFinal.Status);
        Assert.Equal(StatusEquipamento.Funcionando, equipamentoFinal.Status);
        Assert.NotNull(planoFinal.UltimaExecucao);
        Assert.Equal(planoFinal.UltimaExecucao!.Value.AddDays(30), planoFinal.ProximaExecucao);
    }

    // ---------- Gatilho EmAtendimento na ocorrência (2.6, D4 pendente) ----------

    [Fact]
    public async Task GatilhoEmAtendimentoNaOcorrencia()
    {
        // Comportamento atual: Iniciar move a ocorrência para EmAtendimento e, se a conclusão
        // não resolver o problema, a ocorrência volta a Aberta (transição D4 pendente).
        await using var banco = await BancoTeste.CriarAsync();
        var (os, ocorrencia) = await OsEmAtendimentoAsync(banco);

        var emAtendimento = await banco.OpAsync(db => db.Ocorrencias.AsNoTracking().SingleAsync(x => x.Id == ocorrencia.Id));
        Assert.Equal(StatusOcorrencia.EmAtendimento, emAtendimento.Status);

        await ConcluirAsync(banco, os.Id, false);

        var aposConcluir = await banco.OpAsync(db => db.Ocorrencias.AsNoTracking().SingleAsync(x => x.Id == ocorrencia.Id));
        Assert.Equal(StatusOcorrencia.Aberta, aposConcluir.Status);
    }

    // ---------- RegistrarManutencao (2.7) ----------

    [Fact]
    public async Task RegistrarManutencaoDuranteAtendimentoAtualizaCamposComTrim()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var (os, _) = await OsEmAtendimentoAsync(banco);

        await banco.OpAsync(db => new OrdemServicoService(db).RegistrarManutencaoAsync(os.Id, 2,
            new RegistrarManutencaoDto
            {
                Diagnostico = "  Eixo desgastado.  ",
                ServicoRealizado = "  Substituicao do rolamento.  ",
                Observacoes = "  Verificar em 30 dias.  "
            }));

        var manutencao = await banco.OpAsync(db => db.Manutencoes.AsNoTracking().SingleAsync(x => x.OrdemServicoId == os.Id));
        Assert.Equal("Eixo desgastado.", manutencao.Diagnostico);
        Assert.Equal("Substituicao do rolamento.", manutencao.ServicoRealizado);
        Assert.Equal("Verificar em 30 dias.", manutencao.Observacoes);
    }

    [Fact]
    public async Task RegistrarManutencaoForaDeAtendimentoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await CriarOcorrenciaAsync(banco);
        var os = await CriarOsCorretivaAsync(banco, ocorrencia.Id);
        await AssumirAsync(banco, os.Id, 2);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).RegistrarManutencaoAsync(os.Id, 2, new RegistrarManutencaoDto
            { Diagnostico = "Qualquer diagnóstico aqui.", ServicoRealizado = "Nada realizado ainda." })));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("A OS deve estar Em Atendimento.", erro.Message);
    }

    [Fact]
    public async Task RegistrarManutencaoDeDonoErradoLanca403()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var (os, _) = await OsEmAtendimentoAsync(banco);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).RegistrarManutencaoAsync(os.Id, 1, new RegistrarManutencaoDto
            { Diagnostico = "Diagnóstico do intruso.", ServicoRealizado = "Serviço não autorizado." })));
        Assert.Equal(403, erro.StatusHttp);
        Assert.Equal("Somente o técnico responsável pode registrar esta manutenção.", erro.Message);
    }

    [Fact]
    public async Task RegistrarManutencaoOSInexistenteLanca404()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => banco.OpAsync(db =>
            new OrdemServicoService(db).RegistrarManutencaoAsync(999, 2, new RegistrarManutencaoDto
            { Diagnostico = "Diagnóstico inexistente.", ServicoRealizado = "Serviço inexistente." })));
        Assert.Equal(404, erro.StatusHttp);
        Assert.Equal("OS não encontrada.", erro.Message);
    }
}
