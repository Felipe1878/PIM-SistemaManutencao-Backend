using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.Enums;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize(Roles = "Administrador"), Route("api/dashboard")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Resumo() => Ok(new
    {
        equipamentos = new {
            funcionando = await db.Equipamentos.CountAsync(x => x.Ativo && x.Status == StatusEquipamento.Funcionando),
            parados = await db.Equipamentos.CountAsync(x => x.Ativo && x.Status == StatusEquipamento.Parado),
            emManutencao = await db.Equipamentos.CountAsync(x => x.Ativo && x.Status == StatusEquipamento.EmManutencao) },
        ocorrencias = new {
            abertas = await db.Ocorrencias.CountAsync(x => x.Status == StatusOcorrencia.Aberta),
            emAtendimento = await db.Ocorrencias.CountAsync(x => x.Status == StatusOcorrencia.EmAtendimento),
            resolvidas = await db.Ocorrencias.CountAsync(x => x.Status == StatusOcorrencia.Resolvida) },
        ordensServico = new {
            abertas = await db.OrdensServicos.CountAsync(x => x.Status == StatusOrdemServico.Aberta),
            emAtendimento = await db.OrdensServicos.CountAsync(x => x.Status == StatusOrdemServico.EmAtendimento),
            concluidas = await db.OrdensServicos.CountAsync(x => x.Status == StatusOrdemServico.Concluida) }
    });
}
