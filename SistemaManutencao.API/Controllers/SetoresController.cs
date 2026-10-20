using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize, Route("api/setores")]
public class SetoresController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool incluirInativos = false)
        => Ok(await db.Setores.AsNoTracking().Where(x => incluirInativos || x.Ativo).OrderBy(x => x.Nome).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obter(int id)
    {
        var setor = await db.Setores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return setor is null ? NotFound() : Ok(setor);
    }

    [Authorize(Roles = "Administrador"), HttpPost]
    public async Task<IActionResult> Criar([FromBody] SetorDto dto)
    {
        var nome = dto.Nome.Trim();
        if (await db.Setores.AnyAsync(x => x.Nome == nome && x.Ativo))
            throw new RegraNegocioException(409, "Já existe setor ativo com este nome.");
        var setor = new Setor { Nome = nome };
        db.Setores.Add(setor); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = setor.Id }, setor);
    }

    [Authorize(Roles = "Administrador"), HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] SetorDto dto)
    {
        var setor = await db.Setores.FindAsync(id);
        if (setor is null) return NotFound();
        var nome = dto.Nome.Trim();
        if (await db.Setores.AnyAsync(x => x.Id != id && x.Nome == nome && x.Ativo))
            throw new RegraNegocioException(409, "Já existe setor ativo com este nome.");
        setor.Nome = nome; await db.SaveChangesAsync(); return Ok(setor);
    }

    [Authorize(Roles = "Administrador"), HttpPatch("{id:int}/inativar")]
    public async Task<IActionResult> Inativar(int id)
    {
        var setor = await db.Setores.FindAsync(id);
        if (setor is null) return NotFound();
        if (await db.Equipamentos.AnyAsync(x => x.SetorId == id && x.Ativo))
            throw new RegraNegocioException(409, "Inative ou realoque os equipamentos ativos antes de inativar o setor.");
        setor.Ativo = false; await db.SaveChangesAsync(); return Ok(setor);
    }
}
