using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize, Route("api/planos-manutencao")]
public class PlanosManutencaoController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool incluirInativos = false, [FromQuery] bool somenteVencidos = false)
    {
        var q = db.PlanosManutencao.AsNoTracking().AsQueryable();
        if (!incluirInativos) q = q.Where(x => x.Ativo);
        if (somenteVencidos) q = q.Where(x => x.Ativo && x.ProximaExecucao <= DateTime.UtcNow);
        return Ok(await q.OrderBy(x => x.ProximaExecucao).ToListAsync());
    }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obter(int id)
    {
        var p = await db.PlanosManutencao.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return p is null ? NotFound() : Ok(p);
    }
    [Authorize(Roles = "Administrador"), HttpPost]
    public async Task<IActionResult> Criar([FromBody] PlanoManutencaoDto dto)
    {
        await ValidarAsync(dto);
        var p = new PlanoManutencao(); Copiar(dto, p);
        db.PlanosManutencao.Add(p); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = p.Id }, p);
    }
    [Authorize(Roles = "Administrador"), HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] PlanoManutencaoDto dto)
    {
        var p = await db.PlanosManutencao.FindAsync(id);
        if (p is null) return NotFound();
        await ValidarAsync(dto); Copiar(dto, p);
        await db.SaveChangesAsync(); return Ok(p);
    }
    [Authorize(Roles = "Administrador"), HttpPatch("{id:int}/inativar")]
    public async Task<IActionResult> Inativar(int id)
    {
        var p = await db.PlanosManutencao.FindAsync(id);
        if (p is null) return NotFound();
        p.Ativo = false; await db.SaveChangesAsync(); return Ok(p);
    }
    private async Task ValidarAsync(PlanoManutencaoDto dto)
    {
        if (!await db.Equipamentos.AnyAsync(x => x.Id == dto.EquipamentoId && x.Ativo))
            throw new RegraNegocioException(400, "Equipamento ativo não encontrado.");
        if (dto.ProximaExecucao == default)
            throw new RegraNegocioException(400, "Informe a próxima execução.");
        if (dto.UltimaExecucao is not null && dto.ProximaExecucao <= dto.UltimaExecucao)
            throw new RegraNegocioException(400, "Próxima execução deve ser posterior à última execução.");
    }
    private static void Copiar(PlanoManutencaoDto dto, PlanoManutencao p)
    {
        p.EquipamentoId = dto.EquipamentoId;
        p.Descricao = dto.Descricao.Trim(); p.PeriodicidadeDias = dto.PeriodicidadeDias;
        p.UltimaExecucao = dto.UltimaExecucao; p.ProximaExecucao = dto.ProximaExecucao;
    }
}
