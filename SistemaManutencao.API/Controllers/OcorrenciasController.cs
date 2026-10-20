using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Services;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize, Route("api/ocorrencias")]
public class OcorrenciasController(AppDbContext db, OcorrenciaService service) : ControllerBase
{
    [Authorize(Roles = "Administrador,Operador"), HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] int? equipamentoId = null,
        [FromQuery] string? busca = null, [FromQuery] StatusOcorrencia? status = null)
    {
        var q = db.Ocorrencias.AsNoTracking().AsQueryable();
        if (equipamentoId is not null) q = q.Where(x => x.EquipamentoId == equipamentoId);
        if (!string.IsNullOrWhiteSpace(busca))
            q = q.Where(x => x.Descricao.Contains(busca) || x.Equipamento!.Nome.Contains(busca));
        if (status is not null) q = q.Where(x => x.Status == status);
        return Ok(await q.OrderByDescending(x => x.DataRegistro)
            .Select(x => new { x.Id, x.EquipamentoId, Equipamento = x.Equipamento!.Nome,
                x.UsuarioId, x.Descricao, x.Prioridade, x.Status, x.DataRegistro,
                OrdemServicoId = x.OrdemServico == null ? (int?)null : x.OrdemServico.Id })
            .ToListAsync());
    }

    // Tela inicial do Operador (Word §MARCOS-4): ocorrências registradas pelo próprio usuário.
    // Rota literal "minhas" não conflita com {id:int} (constraint de int).
    [Authorize(Roles = "Administrador,Operador"), HttpGet("minhas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Minhas()
    {
        var usuarioId = UsuarioLogado.Id(User);
        return Ok(await db.Ocorrencias.AsNoTracking().Where(x => x.UsuarioId == usuarioId)
            .OrderByDescending(x => x.DataRegistro)
            .Select(x => new { x.Id, x.EquipamentoId, Equipamento = x.Equipamento!.Nome,
                x.UsuarioId, x.Descricao, x.Prioridade, x.Status, x.DataRegistro,
                OrdemServicoId = x.OrdemServico == null ? (int?)null : x.OrdemServico.Id })
            .ToListAsync());
    }

    [Authorize(Roles = "Administrador,Operador"), HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] CriarOcorrenciaDto dto)
    {
        var x = await service.RegistrarAsync(dto, UsuarioLogado.Id(User));
        return CreatedAtAction(nameof(Obter), new { id = x.Id },
            new { x.Id, x.EquipamentoId, x.Descricao, x.Prioridade, x.Status, x.DataRegistro });
    }
}
