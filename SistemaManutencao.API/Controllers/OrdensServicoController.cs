using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Services;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize, Route("api/ordens-servico")]
public class OrdensServicoController(AppDbContext db, OrdemServicoService service) : ControllerBase
{
    [Authorize(Roles = "Administrador,Tecnico"), HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] StatusOrdemServico? status = null)
    {
        var q = db.OrdensServicos.AsNoTracking().AsQueryable();
        if (status is not null) q = q.Where(x => x.Status == status);
        return Ok(await q.OrderByDescending(x => x.DataAbertura)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TecnicoId,
                x.TipoManutencao, x.Status, x.DataAbertura }).ToListAsync());
    }

    [Authorize(Roles = "Administrador,Tecnico"), HttpGet("disponiveis")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Disponiveis()
        => Ok(await db.OrdensServicos.AsNoTracking()
            .Where(x => x.Status == StatusOrdemServico.Aberta && x.TecnicoId == null)
            .OrderBy(x => x.DataAbertura)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TipoManutencao,
                x.Status, x.DataAbertura }).ToListAsync());

    [Authorize(Roles = "Tecnico"), HttpGet("minhas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Minhas()
    {
        var id = UsuarioLogado.Id(User);
        return Ok(await db.OrdensServicos.AsNoTracking().Where(x => x.TecnicoId == id)
            .OrderByDescending(x => x.DataAbertura)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TipoManutencao,
                x.Status, x.DataAbertura }).ToListAsync());
    }

    [Authorize(Roles = "Administrador,Tecnico"), HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(int id)
    {
        var os = await db.OrdensServicos.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TecnicoId,
                x.TipoManutencao, x.Status, x.DataAbertura,
                Manutencao = x.Manutencao == null ? null : new { x.Manutencao.Id, x.Manutencao.Diagnostico,
                    x.Manutencao.ServicoRealizado, x.Manutencao.Observacoes,
                    x.Manutencao.DataInicio, x.Manutencao.DataConclusao, x.Manutencao.ProblemaResolvido } })
            .SingleOrDefaultAsync();
        return os is null ? NotFound() : Ok(os);
    }

    [Authorize(Roles = "Administrador"), HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] CriarOrdemServicoDto dto)
    {
        var os = await service.CriarAsync(dto);
        return CreatedAtAction(nameof(Obter), new { id = os.Id }, new { os.Id, os.Status,
            os.OcorrenciaId, os.PlanoManutencaoId, os.TipoManutencao });
    }

    // Nota: OS inexistente devolve 409 (regra do service), não 404.
    [Authorize(Roles = "Tecnico"), HttpPost("{id:int}/assumir")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assumir(int id)
    {
        var os = await service.AssumirAsync(id, UsuarioLogado.Id(User));
        return Ok(new { os.Id, os.TecnicoId, os.Status });
    }

    [Authorize(Roles = "Tecnico"), HttpPost("{id:int}/iniciar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Iniciar(int id)
    {
        var os = await service.IniciarAsync(id, UsuarioLogado.Id(User));
        return Ok(new { os.Id, os.TecnicoId, os.Status, dataInicio = os.Manutencao?.DataInicio });
    }

    [Authorize(Roles = "Tecnico"), HttpPut("{id:int}/manutencao")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegistrarManutencao(int id, [FromBody] RegistrarManutencaoDto dto)
    {
        var m = await service.RegistrarManutencaoAsync(id, UsuarioLogado.Id(User), dto);
        return Ok(new { m.Id, m.OrdemServicoId, m.Diagnostico, m.ServicoRealizado,
            m.Observacoes, m.DataInicio });
    }

    [Authorize(Roles = "Tecnico"), HttpPost("{id:int}/concluir")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Concluir(int id, [FromBody] ConcluirOrdemServicoDto dto)
    {
        var os = await service.ConcluirAsync(id, UsuarioLogado.Id(User), dto);
        return Ok(new { os.Id, os.Status, problemaResolvido = os.Manutencao?.ProblemaResolvido,
            dataInicio = os.Manutencao?.DataInicio, dataConclusao = os.Manutencao?.DataConclusao });
    }

    [Authorize(Roles = "Administrador"), HttpPost("{id:int}/cancelar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(int id)
    {
        var os = await service.CancelarAsync(id);
        return Ok(new { os.Id, os.Status });
    }
}
