using System.Data;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Services;

public class OcorrenciaService(AppDbContext db)
{
    public async Task<Ocorrencia> RegistrarAsync(CriarOcorrenciaDto dto, int usuarioId)
    {
        if (!Enum.IsDefined(dto.Prioridade))
            throw new RegraNegocioException(400, "Prioridade inválida.");
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var equipamento = await db.Equipamentos.FindAsync(dto.EquipamentoId)
            ?? throw new RegraNegocioException(404, "Equipamento não encontrado.");
        if (!equipamento.Ativo) throw new RegraNegocioException(409, "Equipamento inativo.");
        if (await db.Ocorrencias.AnyAsync(x => x.EquipamentoId == dto.EquipamentoId && x.Status != StatusOcorrencia.Resolvida))
            throw new RegraNegocioException(409, "O equipamento já possui uma ocorrência ativa.");
        if (equipamento.Status != StatusEquipamento.Funcionando)
            throw new RegraNegocioException(409, "O equipamento precisa estar Funcionando antes de abrir nova ocorrência.");
        var ocorrencia = new Ocorrencia { EquipamentoId = equipamento.Id, UsuarioId = usuarioId,
            Descricao = dto.Descricao.Trim(), Prioridade = dto.Prioridade,
            Status = StatusOcorrencia.Aberta, DataRegistro = DateTime.UtcNow };
        equipamento.Status = StatusEquipamento.Parado;
        db.Ocorrencias.Add(ocorrencia);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return ocorrencia;
    }
}
