using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Controllers;

[ApiController, Authorize(Roles = "Administrador"), Route("api/usuarios")]
public class UsuariosController(AppDbContext db, IPasswordHasher<Usuario> hasher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] bool incluirInativos = false)
    {
        var usuarios = await db.Usuarios.AsNoTracking().Where(x => incluirInativos || x.Ativo)
            .OrderBy(x => x.Nome).ToListAsync();
        return Ok(usuarios.Select(UsuarioResposta.De));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(int id)
    {
        var u = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return u is null ? NotFound() : Ok(UsuarioResposta.De(u));
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioDto dto)
    {
        var perfil = ValidarPerfil(dto.Perfil, dto.Funcao);
        var email = dto.Email.Trim().ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(x => x.Email == email))
            throw new RegraNegocioException(409, "Email já cadastrado.");
        var u = new Usuario { Nome = dto.Nome.Trim(), Email = email, Perfil = perfil,
            Funcao = perfil == PerfilUsuario.Administrador ? null : dto.Funcao };
        u.SenhaHash = hasher.HashPassword(u, dto.Senha);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Obter), new { id = u.Id }, UsuarioResposta.De(u));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarUsuarioDto dto)
    {
        var perfil = ValidarPerfil(dto.Perfil, dto.Funcao);
        var u = await db.Usuarios.FindAsync(id);
        if (u is null) return NotFound();
        if (u.Funcao == FuncaoFuncionario.Tecnico && (perfil != PerfilUsuario.Funcionario || dto.Funcao != FuncaoFuncionario.Tecnico) &&
            await db.OrdensServicos.AnyAsync(x => x.TecnicoId == id &&
                (x.Status == StatusOrdemServico.Aberta || x.Status == StatusOrdemServico.EmAtendimento)))
            throw new RegraNegocioException(409, "Técnico possui OS pendente; conclua ou transfira antes de alterar a função.");
        u.Nome = dto.Nome.Trim(); u.Perfil = perfil;
        u.Funcao = perfil == PerfilUsuario.Administrador ? null : dto.Funcao;
        await db.SaveChangesAsync();
        return Ok(UsuarioResposta.De(u));
    }

    [HttpPatch("{id:int}/inativar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Inativar(int id)
    {
        if (UsuarioLogado.Id(User) == id)
            throw new RegraNegocioException(409, "Não é permitido inativar a própria conta.");
        var u = await db.Usuarios.FindAsync(id);
        if (u is null) return NotFound();
        if (await db.OrdensServicos.AnyAsync(x => x.TecnicoId == id &&
            (x.Status == StatusOrdemServico.Aberta || x.Status == StatusOrdemServico.EmAtendimento)))
            throw new RegraNegocioException(409, "Técnico possui OS pendente; conclua antes de inativar.");
        u.Ativo = false;
        await db.SaveChangesAsync();
        return Ok(UsuarioResposta.De(u));
    }

    // Perfil agora é anulável no DTO (elimina o default silencioso Administrador = 0):
    // omissão/null no JSON é barrada pelo ModelState com 400 automático ([Required] em Nullable);
    // o guard abaixo é defensivo para chamadas diretas ao método.
    private static PerfilUsuario ValidarPerfil(PerfilUsuario? perfil, FuncaoFuncionario? funcao)
    {
        if (perfil is null) throw new RegraNegocioException(400, "Informe o perfil.");
        if (!Enum.IsDefined(perfil.Value)) throw new RegraNegocioException(400, "Perfil inválido.");
        if (perfil.Value == PerfilUsuario.Funcionario && (funcao is null || !Enum.IsDefined(funcao.Value)))
            throw new RegraNegocioException(400, "Funcionário exige função Operador ou Técnico.");
        if (perfil.Value == PerfilUsuario.Administrador && funcao is not null)
            throw new RegraNegocioException(400, "Administrador não deve possuir função de funcionário.");
        return perfil.Value;
    }
}
