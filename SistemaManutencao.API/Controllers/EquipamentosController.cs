using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize, Route("api/equipamentos")]
public class EquipamentosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Listar([FromQuery] string? busca = null, [FromQuery] bool incluirInativos = false)
    {
        var q = db.Equipamentos.AsNoTracking().AsQueryable();
        if (!incluirInativos) q = q.Where(x => x.Ativo);
        if (!string.IsNullOrWhiteSpace(busca)) q = q.Where(x => x.Nome.Contains(busca) || x.Patrimonio.Contains(busca));
        return Ok(await q.Select(x => new { x.Id, x.Nome, x.Patrimonio, x.Fabricante,
            x.Modelo, x.NumeroSerie, x.DataAquisicao, x.Status, x.SetorId,
            Setor = x.Setor!.Nome, x.Ativo }).OrderBy(x => x.Nome).ToListAsync());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(int id)
    {
        var e = await db.Equipamentos.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Nome, x.Patrimonio, x.Fabricante, x.Modelo,
                x.NumeroSerie, x.DataAquisicao, x.Status, x.SetorId, Setor = x.Setor!.Nome, x.Ativo })
            .SingleOrDefaultAsync();
        return e is null ? NotFound() : Ok(e);
    }

    [Authorize(Roles = "Administrador"), HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] EquipamentoDto dto)
    {
        await ValidarAsync(dto, null);
        var e = new Equipamento(); Copiar(dto, e);
        e.Status = StatusEquipamento.Funcionando; e.Ativo = true;
        db.Equipamentos.Add(e); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = e.Id }, new { e.Id, e.Patrimonio, e.Status });
    }

    [Authorize(Roles = "Administrador"), HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(int id, [FromBody] EquipamentoDto dto)
    {
        var e = await db.Equipamentos.FindAsync(id);
        if (e is null) return NotFound();
        await ValidarAsync(dto, id); Copiar(dto, e);
        await db.SaveChangesAsync(); return Ok(new { e.Id, e.Patrimonio, e.Status, e.SetorId, e.Ativo });
    }

    [Authorize(Roles = "Administrador"), HttpPatch("{id:int}/inativar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Inativar(int id)
    {
        var e = await db.Equipamentos.FindAsync(id);
        if (e is null) return NotFound();
        if (await db.Ocorrencias.AnyAsync(x => x.EquipamentoId == id && x.Status != StatusOcorrencia.Resolvida))
            throw new RegraNegocioException(409, "Equipamento com ocorrência ativa não pode ser inativado.");
        if (await db.OrdensServicos.AnyAsync(x => (x.Ocorrencia != null && x.Ocorrencia.EquipamentoId == id ||
            x.PlanoManutencao != null && x.PlanoManutencao.EquipamentoId == id) &&
            (x.Status == StatusOrdemServico.Aberta || x.Status == StatusOrdemServico.EmAtendimento)))
            throw new RegraNegocioException(409, "Equipamento possui OS em aberto.");
        e.Ativo = false; await db.SaveChangesAsync(); return Ok(new { e.Id, e.Ativo });
    }

    private async Task ValidarAsync(EquipamentoDto dto, int? id)
    {
        if (!await db.Setores.AnyAsync(x => x.Id == dto.SetorId && x.Ativo))
            throw new RegraNegocioException(400, "Setor ativo não encontrado.");
        if (await db.Equipamentos.AnyAsync(x => x.Patrimonio == dto.Patrimonio.Trim() && x.Id != id))
            throw new RegraNegocioException(409, "Patrimônio já cadastrado.");
    }
    private static void Copiar(EquipamentoDto dto, Equipamento e)
    {
        e.Nome = dto.Nome.Trim(); e.Patrimonio = dto.Patrimonio.Trim();
        e.Fabricante = dto.Fabricante?.Trim(); e.Modelo = dto.Modelo?.Trim();
        e.NumeroSerie = dto.NumeroSerie?.Trim(); e.DataAquisicao = dto.DataAquisicao;
        e.SetorId = dto.SetorId;
    }
}
