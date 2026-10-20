using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;
using SistemaManutencao.API.Services;
using Xunit;

namespace SistemaManutencao.Tests;

public class RegrasBackendTests
{
    private static async Task<(SqliteConnection, AppDbContext)> CriarBanco()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Usuarios.AddRange(
            new Usuario { Id = 1, Nome = "Operador", Email = "oper@pim.local", SenhaHash = "teste", Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Operador },
            new Usuario { Id = 2, Nome = "Tecnico", Email = "tec@pim.local", SenhaHash = "teste", Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Tecnico });
        db.Setores.Add(new Setor { Id = 1, Nome = "Producao" });
        db.Equipamentos.Add(new Equipamento { Id = 1, Nome = "Prensa", Patrimonio = "P01", SetorId = 1 });
        await db.SaveChangesAsync();
        return (conn, db);
    }

    [Fact]
    public async Task RegistroDeOcorrenciaParaEquipamentoEFicaParado()
    {
        var (conn, db) = await CriarBanco();
        await using (conn) await using (db)
        {
            var service = new OcorrenciaService(db);
            var ocorrencia = await service.RegistrarAsync(new CriarOcorrenciaDto
            {
                EquipamentoId = 1, Descricao = "Prensa parou de ligar.", Prioridade = PrioridadeOcorrencia.Alta
            }, 1);
            Assert.Equal(StatusOcorrencia.Aberta, ocorrencia.Status);
            Assert.Equal(StatusEquipamento.Parado, (await db.Equipamentos.FindAsync(1))!.Status);
        }
    }

    [Fact]
    public async Task NaoPermiteDuasOcorrenciasAtivas()
    {
        var (conn, db) = await CriarBanco();
        await using (conn) await using (db)
        {
            var service = new OcorrenciaService(db);
            var dto = new CriarOcorrenciaDto { EquipamentoId = 1,
                Descricao = "Falha de energia.", Prioridade = PrioridadeOcorrencia.Alta };
            await service.RegistrarAsync(dto, 1);
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => service.RegistrarAsync(dto, 1));
            Assert.Equal(409, erro.StatusHttp);
        }
    }

    [Theory]
    [InlineData(true, StatusOcorrencia.Resolvida, StatusEquipamento.Funcionando)]
    [InlineData(false, StatusOcorrencia.Aberta, StatusEquipamento.Parado)]
    public async Task ConclusaoDeOSRespeitaResultado(bool resolvido, StatusOcorrencia statusOcorrencia,
        StatusEquipamento statusEquipamento)
    {
        var (conn, db) = await CriarBanco();
        await using (conn) await using (db)
        {
            var ocorrencia = await new OcorrenciaService(db).RegistrarAsync(new CriarOcorrenciaDto
            { EquipamentoId = 1, Descricao = "Problema no motor.", Prioridade = PrioridadeOcorrencia.Alta }, 1);
            var service = new OrdemServicoService(db);
            var os = await service.CriarAsync(new CriarOrdemServicoDto
            { TipoManutencao = TipoManutencao.Corretiva, OcorrenciaId = ocorrencia.Id });
            await service.AssumirAsync(os.Id, 2);
            await service.IniciarAsync(os.Id, 2);
            await service.ConcluirAsync(os.Id, 2, new ConcluirOrdemServicoDto
            { Diagnostico = "Motor com falha", ServicoRealizado = "Verificacao de conexoes", ProblemaResolvido = resolvido });
            Assert.Equal(StatusOrdemServico.Concluida, (await db.OrdensServicos.FindAsync(os.Id))!.Status);
            Assert.Equal(statusOcorrencia, (await db.Ocorrencias.FindAsync(ocorrencia.Id))!.Status);
            Assert.Equal(statusEquipamento, (await db.Equipamentos.FindAsync(1))!.Status);
        }
    }

    [Fact]
    public async Task TecnicoDiferenteNaoPodeIniciarOS()
    {
        var (conn, db) = await CriarBanco();
        await using (conn) await using (db)
        {
            var ocorrencia = await new OcorrenciaService(db).RegistrarAsync(new CriarOcorrenciaDto
            { EquipamentoId = 1, Descricao = "Falha de motor", Prioridade = PrioridadeOcorrencia.Alta }, 1);
            var service = new OrdemServicoService(db);
            var os = await service.CriarAsync(new CriarOrdemServicoDto
            { TipoManutencao = TipoManutencao.Corretiva, OcorrenciaId = ocorrencia.Id });
            await service.AssumirAsync(os.Id, 2);
            var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => service.IniciarAsync(os.Id, 1));
            Assert.Equal(403, erro.StatusHttp);
        }
    }
}
