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
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] StatusOrdemServico? status = null)
    {
        var q = db.OrdensServicos.AsNoTracking().AsQueryable();
        if (status is not null) q = q.Where(x => x.Status == status);
        return Ok(await q.OrderByDescending(x => x.DataAbertura)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TecnicoId,
                x.TipoManutencao, x.Status, x.DataAbertura }).ToListAsync());
    }
    [HttpGet("disponiveis")]
    public async Task<IActionResult> Disponiveis()
        => Ok(await db.OrdensServicos.AsNoTracking()
            .Where(x => x.Status == StatusOrdemServico.Aberta && x.TecnicoId == null)
            .OrderBy(x => x.DataAbertura)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TipoManutencao,
                x.Status, x.DataAbertura }).ToListAsync());

    [Authorize(Roles = "Tecnico"), HttpGet("minhas")]
    public async Task<IActionResult> Minhas()
    {
        var id = UsuarioLogado.Id(User);
        return Ok(await db.OrdensServicos.AsNoTracking().Where(x => x.TecnicoId == id)
            .OrderByDescending(x => x.DataAbertura)
            .Select(x => new { x.Id, x.OcorrenciaId, x.PlanoManutencaoId, x.TipoManutencao,
                x.Status, x.DataAbertura }).ToListAsync());
    }
    [HttpGet("{id:int}")]
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
    public async Task<IActionResult> Criar([FromBody] CriarOrdemServicoDto dto)
    {
        var os = await service.CriarAsync(dto);
        return CreatedAtAction(nameof(Obter), new { id = os.Id }, new { os.Id, os.Status,
            os.OcorrenciaId, os.PlanoManutencaoId, os.TipoManutencao });
    }
    [Authorize(Roles = "Tecnico"), HttpPost("{id:int}/assumir")]
    public async Task<IActionResult> Assumir(int id)
    {
        var os = await service.AssumirAsync(id, UsuarioLogado.Id(User));
        return Ok(new { os.Id, os.TecnicoId, os.Status });
    }
    [Authorize(Roles = "Tecnico"), HttpPost("{id:int}/iniciar")]
    public async Task<IActionResult> Iniciar(int id)
    {
        var os = await service.IniciarAsync(id, UsuarioLogado.Id(User));
        return Ok(new { os.Id, os.TecnicoId, os.Status, dataInicio = os.Manutencao?.DataInicio });
    }
    [Authorize(Roles = "Tecnico"), HttpPut("{id:int}/manutencao")]
    public async Task<IActionResult> RegistrarManutencao(int id, [FromBody] RegistrarManutencaoDto dto)
    {
        var m = await service.RegistrarManutencaoAsync(id, UsuarioLogado.Id(User), dto);
        return Ok(new { m.Id, m.OrdemServicoId, m.Diagnostico, m.ServicoRealizado,
            m.Observacoes, m.DataInicio });
    }
    [Authorize(Roles = "Tecnico"), HttpPost("{id:int}/concluir")]
    public async Task<IActionResult> Concluir(int id, [FromBody] ConcluirOrdemServicoDto dto)
    {
        var os = await service.ConcluirAsync(id, UsuarioLogado.Id(User), dto);
        return Ok(new { os.Id, os.Status, problemaResolvido = os.Manutencao?.ProblemaResolvido,
            dataInicio = os.Manutencao?.DataInicio, dataConclusao = os.Manutencao?.DataConclusao });
    }
    [Authorize(Roles = "Administrador"), HttpPost("{id:int}/cancelar")]
    public async Task<IActionResult> Cancelar(int id)
    {
        var os = await service.CancelarAsync(id);
        return Ok(new { os.Id, os.Status });
    }
}
