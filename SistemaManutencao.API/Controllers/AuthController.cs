using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Services;

namespace SistemaManutencao.API.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AuthService auth, AppDbContext db) : ControllerBase
{
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto) => Ok(await auth.LoginAsync(dto));

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var u = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == UsuarioLogado.Id(User) && x.Ativo);
        return u is null ? Unauthorized() : Ok(UsuarioResposta.De(u));
    }
}
