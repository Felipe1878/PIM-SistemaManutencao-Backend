using System.Data;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Services;

public class OrdemServicoService(AppDbContext db)
{
    public async Task<OrdemServico> CriarAsync(CriarOrdemServicoDto dto)
    {
        // Seam de compilação (SUB A): DTO com enum anulável exige null-guard defensivo.
        if (dto.TipoManutencao is null)
            throw new RegraNegocioException(400, "Informe o tipo de manutenção.");
        if (!Enum.IsDefined(dto.TipoManutencao.Value))
            throw new RegraNegocioException(400, "Tipo de manutenção inválido.");
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var os = new OrdemServico { TipoManutencao = dto.TipoManutencao.Value,
            Status = StatusOrdemServico.Aberta, DataAbertura = DateTime.UtcNow };
        if (dto.TipoManutencao.Value == TipoManutencao.Corretiva)
        {
            if (dto.OcorrenciaId is null || dto.PlanoManutencaoId is not null)
                throw new RegraNegocioException(400, "OS corretiva exige ocorrenciaId e não aceita planoManutencaoId.");
            var ocorrencia = await db.Ocorrencias.FindAsync(dto.OcorrenciaId.Value)
                ?? throw new RegraNegocioException(404, "Ocorrência não encontrada.");
            if (ocorrencia.Status == StatusOcorrencia.Resolvida)
                throw new RegraNegocioException(409, "Ocorrência já resolvida.");
            if (await db.OrdensServicos.AnyAsync(x => x.OcorrenciaId == ocorrencia.Id))
                throw new RegraNegocioException(409, "Ocorrência já possui OS (relação 1:1).");
            os.OcorrenciaId = ocorrencia.Id;
        }
        else
        {
            if (dto.PlanoManutencaoId is null || dto.OcorrenciaId is not null)
                throw new RegraNegocioException(400, "OS preventiva exige planoManutencaoId e não aceita ocorrenciaId.");
            var plano = await db.PlanosManutencao.Include(x => x.Equipamento)
                .SingleOrDefaultAsync(x => x.Id == dto.PlanoManutencaoId.Value)
                ?? throw new RegraNegocioException(404, "Plano preventivo não encontrado.");
            if (!plano.Ativo || plano.Equipamento?.Ativo != true)
                throw new RegraNegocioException(409, "Plano ou equipamento inativo.");
            if (plano.Equipamento.Status != StatusEquipamento.Funcionando ||
                await db.Ocorrencias.AnyAsync(x => x.EquipamentoId == plano.EquipamentoId && x.Status != StatusOcorrencia.Resolvida))
                throw new RegraNegocioException(409, "Preventiva requer equipamento Funcionando e sem ocorrência ativa.");
            if (await db.OrdensServicos.AnyAsync(x => x.PlanoManutencaoId == plano.Id &&
                (x.Status == StatusOrdemServico.Aberta || x.Status == StatusOrdemServico.EmAtendimento)))
                throw new RegraNegocioException(409, "Já existe OS preventiva não finalizada para esse plano.");
            os.PlanoManutencaoId = plano.Id;
        }
        db.OrdensServicos.Add(os);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return os;
    }

    public async Task<OrdemServico> AssumirAsync(int id, int tecnicoId)
    {
        // UPDATE com predicado garante que dois técnicos não assumam a mesma OS ao mesmo tempo.
        var linhas = await db.OrdensServicos.Where(x => x.Id == id && x.TecnicoId == null
            && x.Status == StatusOrdemServico.Aberta)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TecnicoId, tecnicoId));
        if (linhas == 0) throw new RegraNegocioException(409, "OS inexistente, ocupada ou indisponível.");
        return (await db.OrdensServicos.AsNoTracking().SingleAsync(x => x.Id == id));
    }

    public async Task<OrdemServico> IniciarAsync(int id, int tecnicoId, DateTime? dataInicio = null)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var os = await db.OrdensServicos.Include(x => x.Ocorrencia).ThenInclude(x => x!.Equipamento)
            .Include(x => x.PlanoManutencao).ThenInclude(x => x!.Equipamento)
            .Include(x => x.Manutencao).SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RegraNegocioException(404, "OS não encontrada.");
        if (os.TecnicoId != tecnicoId) throw new RegraNegocioException(403, "Somente o técnico responsável pode iniciar esta OS.");
        if (os.Status != StatusOrdemServico.Aberta || os.Manutencao is not null)
            throw new RegraNegocioException(409, "OS não está aberta ou já foi iniciada.");
        ValidarUtc(dataInicio, "Data de início");
        var equipamento = ObterEquipamento(os);
        if (!equipamento.Ativo) throw new RegraNegocioException(409, "Equipamento inativo.");
        if (os.Ocorrencia is not null)
        {
            if (os.Ocorrencia.Status != StatusOcorrencia.Aberta || equipamento.Status != StatusEquipamento.Parado)
                throw new RegraNegocioException(409, "Ocorrência ou equipamento em estado incompatível.");
            os.Ocorrencia.Status = StatusOcorrencia.EmAtendimento;
        }
        else if (equipamento.Status != StatusEquipamento.Funcionando)
            throw new RegraNegocioException(409, "Preventiva só pode iniciar com equipamento Funcionando.");
        equipamento.Status = StatusEquipamento.EmManutencao;
        os.Status = StatusOrdemServico.EmAtendimento;
        db.Manutencoes.Add(new Manutencao { OrdemServicoId = id, DataInicio = dataInicio ?? DateTime.UtcNow });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return os;
    }

    public async Task<Manutencao> RegistrarManutencaoAsync(int id, int tecnicoId, RegistrarManutencaoDto dto)
    {
        var os = await db.OrdensServicos.Include(x => x.Manutencao)
            .SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RegraNegocioException(404, "OS não encontrada.");
        if (os.TecnicoId != tecnicoId)
            throw new RegraNegocioException(403, "Somente o técnico responsável pode registrar esta manutenção.");
        if (os.Status != StatusOrdemServico.EmAtendimento || os.Manutencao is null)
            throw new RegraNegocioException(409, "A OS deve estar Em Atendimento.");
        var m = os.Manutencao;
        m.Diagnostico = dto.Diagnostico.Trim();
        m.ServicoRealizado = dto.ServicoRealizado.Trim();
        m.Observacoes = dto.Observacoes?.Trim();
        await db.SaveChangesAsync();
        return m;
    }

    public async Task<OrdemServico> ConcluirAsync(int id, int tecnicoId, ConcluirOrdemServicoDto dto)
    {
        if (dto.ProblemaResolvido is null) throw new RegraNegocioException(400, "Informe problemaResolvido.");
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var os = await db.OrdensServicos.Include(x => x.Ocorrencia).ThenInclude(x => x!.Equipamento)
            .Include(x => x.PlanoManutencao).ThenInclude(x => x!.Equipamento)
            .Include(x => x.Manutencao).SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RegraNegocioException(404, "OS não encontrada.");
        if (os.TecnicoId != tecnicoId) throw new RegraNegocioException(403, "Somente o técnico responsável pode concluir esta OS.");
        if (os.Status != StatusOrdemServico.EmAtendimento || os.Manutencao is null)
            throw new RegraNegocioException(409, "A OS precisa estar Em Atendimento para ser concluída.");
        var manutencao = os.Manutencao;
        var diagnostico = string.IsNullOrWhiteSpace(dto.Diagnostico) ? manutencao.Diagnostico : dto.Diagnostico.Trim();
        var servicoRealizado = string.IsNullOrWhiteSpace(dto.ServicoRealizado) ? manutencao.ServicoRealizado : dto.ServicoRealizado.Trim();
        if (diagnostico.Length < 5 || servicoRealizado.Length < 5)
            throw new RegraNegocioException(400, "Informe diagnóstico e serviço realizado na manutenção ou na conclusão.");
        var dataConclusao = dto.DataConclusao ?? DateTime.UtcNow;
        ValidarUtc(dto.DataConclusao, "Data de conclusão");
        if (dataConclusao < manutencao.DataInicio)
            throw new RegraNegocioException(400, "Data de conclusão não pode ser anterior à data de início.");
        DateTime? proximaExecucao = null;
        if (os.PlanoManutencao is not null && dto.ProblemaResolvido.Value)
        {
            try { proximaExecucao = dataConclusao.AddDays(os.PlanoManutencao.PeriodicidadeDias); }
            catch (ArgumentOutOfRangeException)
            {
                throw new RegraNegocioException(400, "Data de conclusão e periodicidade ultrapassam o limite da próxima execução.");
            }
        }
        manutencao.Diagnostico = diagnostico;
        manutencao.ServicoRealizado = servicoRealizado;
        if (dto.Observacoes is not null) manutencao.Observacoes = dto.Observacoes.Trim();
        manutencao.ProblemaResolvido = dto.ProblemaResolvido.Value;
        manutencao.DataConclusao = dataConclusao;
        os.Status = StatusOrdemServico.Concluida;
        var equipamento = ObterEquipamento(os);
        equipamento.Status = dto.ProblemaResolvido.Value ? StatusEquipamento.Funcionando : StatusEquipamento.Parado;
        if (os.Ocorrencia is not null)
            os.Ocorrencia.Status = dto.ProblemaResolvido.Value ? StatusOcorrencia.Resolvida : StatusOcorrencia.Aberta;
        if (os.PlanoManutencao is not null && dto.ProblemaResolvido.Value)
        {
            os.PlanoManutencao.UltimaExecucao = manutencao.DataConclusao;
            os.PlanoManutencao.ProximaExecucao = proximaExecucao!.Value;
        }
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return os;
    }

    public async Task<OrdemServico> CancelarAsync(int id)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var os = await db.OrdensServicos.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RegraNegocioException(404, "OS não encontrada.");
        if (os.Status != StatusOrdemServico.Aberta)
            throw new RegraNegocioException(409, "Apenas OS aberta pode ser cancelada.");
        os.Status = StatusOrdemServico.Cancelada;
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return os;
    }

    private static Equipamento ObterEquipamento(OrdemServico os)
        => os.Ocorrencia?.Equipamento ?? os.PlanoManutencao?.Equipamento
            ?? throw new RegraNegocioException(409, "OS sem equipamento vinculado.");

    private static void ValidarUtc(DateTime? data, string campo)
    {
        if (data is not null && data.Value.Kind != DateTimeKind.Utc)
            throw new RegraNegocioException(400, $"{campo} deve estar em UTC (sufixo Z).");
    }
}
