using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;
using SistemaManutencao.API.Services;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Testes de caracterização do OcorrenciaService (comportamento ATUAL).
/// Leituras de verificação usam contexto novo + AsNoTracking (fora do tracker).
/// </summary>
public class OcorrenciaServiceTests
{
    private static Task RegistrarAsync(BancoTeste banco, CriarOcorrenciaDto dto, int usuarioId = 1)
        => banco.OpAsync(db => new OcorrenciaService(db).RegistrarAsync(dto, usuarioId));

    [Fact]
    public async Task RegistroDeixaEquipamentoParadoEOcorrenciaAbertaPersistidos()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var ocorrencia = await banco.OpAsync(db => new OcorrenciaService(db).RegistrarAsync(
            new CriarOcorrenciaDto { EquipamentoId = 1, Descricao = "  Prensa parou de ligar.  ",
                Prioridade = PrioridadeOcorrencia.Alta }, 1));

        var persistido = await banco.OpAsync(db => db.Ocorrencias.AsNoTracking().SingleAsync(x => x.Id == ocorrencia.Id));
        var equipamento = await banco.OpAsync(db => db.Equipamentos.AsNoTracking().SingleAsync(x => x.Id == 1));

        Assert.Equal(StatusOcorrencia.Aberta, persistido.Status);
        Assert.Equal(PrioridadeOcorrencia.Alta, persistido.Prioridade);
        Assert.Equal("Prensa parou de ligar.", persistido.Descricao); // Trim aplicado pelo service
        Assert.Equal(StatusEquipamento.Parado, equipamento.Status);
    }

    [Fact]
    public async Task NaoPermiteDuasOcorrenciasAtivasNoMesmoEquipamento()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var dto = new CriarOcorrenciaDto { EquipamentoId = 1,
            Descricao = "Falha de energia.", Prioridade = PrioridadeOcorrencia.Alta };
        await RegistrarAsync(banco, dto);

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => RegistrarAsync(banco, dto));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("O equipamento já possui uma ocorrência ativa.", erro.Message);
    }

    [Fact]
    public async Task PrioridadeForaDoEnumLanca400()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => RegistrarAsync(banco,
            new CriarOcorrenciaDto { EquipamentoId = 1, Descricao = "Qualquer falha registrada.",
                Prioridade = (PrioridadeOcorrencia)99 }));
        Assert.Equal(400, erro.StatusHttp);
        Assert.Equal("Prioridade inválida.", erro.Message);
    }

    [Fact]
    public async Task EquipamentoInexistenteLanca404()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => RegistrarAsync(banco,
            new CriarOcorrenciaDto { EquipamentoId = 999, Descricao = "Falha em equipamento fantasma.",
                Prioridade = PrioridadeOcorrencia.Baixa }));
        Assert.Equal(404, erro.StatusHttp);
        Assert.Equal("Equipamento não encontrado.", erro.Message);
    }

    [Fact]
    public async Task EquipamentoInativoLanca409()
    {
        await using var banco = await BancoTeste.CriarAsync();
        await banco.OpAsync(db =>
        {
            db.Equipamentos.Add(new Equipamento { Id = 2, Nome = "Torno", Patrimonio = "T01",
                SetorId = 1, Ativo = false });
            return db.SaveChangesAsync();
        });

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => RegistrarAsync(banco,
            new CriarOcorrenciaDto { EquipamentoId = 2, Descricao = "Torno fora de linea.",
                Prioridade = PrioridadeOcorrencia.Media }));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("Equipamento inativo.", erro.Message);
    }

    [Fact]
    public async Task EquipamentoNaoFuncionandoLanca409()
    {
        // Equipamento Parado sem ocorrência ativa: isola a regra de status da regra de duplicidade.
        await using var banco = await BancoTeste.CriarAsync();
        await banco.OpAsync(db =>
        {
            db.Equipamentos.Add(new Equipamento { Id = 2, Nome = "Torno", Patrimonio = "T01",
                SetorId = 1, Status = StatusEquipamento.Parado });
            return db.SaveChangesAsync();
        });

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => RegistrarAsync(banco,
            new CriarOcorrenciaDto { EquipamentoId = 2, Descricao = "Torno continua travado.",
                Prioridade = PrioridadeOcorrencia.Media }));
        Assert.Equal(409, erro.StatusHttp);
        Assert.Equal("O equipamento precisa estar Funcionando antes de abrir nova ocorrência.", erro.Message);
    }
}
