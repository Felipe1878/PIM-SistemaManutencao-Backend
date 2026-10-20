using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Services;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize, Route("api/ocorrencias")]
public class OcorrenciasController(AppDbContext db, OcorrenciaService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int? equipamentoId = null)
    {
        var q = db.Ocorrencias.AsNoTracking().AsQueryable();
        if (equipamentoId is not null) q = q.Where(x => x.EquipamentoId == equipamentoId);
        return Ok(await q.OrderByDescending(x => x.DataRegistro)
            .Select(x => new { x.Id, x.EquipamentoId, Equipamento = x.Equipamento!.Nome,
                x.UsuarioId, x.Descricao, x.Prioridade, x.Status, x.DataRegistro,
                OrdemServicoId = x.OrdemServico == null ? (int?)null : x.OrdemServico.Id })
            .ToListAsync());
    }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obter(int id)
    {
        var x = await db.Ocorrencias.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.EquipamentoId, Equipamento = x.Equipamento!.Nome,
                x.UsuarioId, x.Descricao, x.Prioridade, x.Status, x.DataRegistro,
                OrdemServicoId = x.OrdemServico == null ? (int?)null : x.OrdemServico.Id })
            .SingleOrDefaultAsync();
        return x is null ? NotFound() : Ok(x);
    }
    [Authorize(Roles = "Administrador,Operador"), HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarOcorrenciaDto dto)
    {
        var x = await service.RegistrarAsync(dto, UsuarioLogado.Id(User));
        return CreatedAtAction(nameof(Obter), new { id = x.Id },
            new { x.Id, x.EquipamentoId, x.Descricao, x.Prioridade, x.Status, x.DataRegistro });
    }
}
