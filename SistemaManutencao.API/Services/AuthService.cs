using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Services;

public class AuthService(AppDbContext db, IPasswordHasher<Usuario> hasher, IConfiguration config)
{
    // Usuário e hash fictícios usados no login de email inexistente: executam o mesmo PBKDF2
    // de verificação do caminho real, mantendo o tempo de resposta constante (impede
    // enumerar emails cadastrados medindo o tempo de resposta — correção de segurança).
    private static readonly Usuario UsuarioVerificacao = new() { Nome = "Verificacao", Email = "verificacao@invalido" };
    private static string? _hashVerificacao;

    public async Task<object> LoginAsync(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var u = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email && x.Ativo);
        if (u is null)
        {
            _hashVerificacao ??= hasher.HashPassword(UsuarioVerificacao, "hash-padrao-de-timing");
            _ = hasher.VerifyHashedPassword(UsuarioVerificacao, _hashVerificacao, dto.Senha);
            throw new RegraNegocioException(401, "Email ou senha inválidos.");
        }
        if (hasher.VerifyHashedPassword(u, u.SenhaHash, dto.Senha) == PasswordVerificationResult.Failed)
            throw new RegraNegocioException(401, "Email ou senha inválidos.");

        var papel = u.Perfil == PerfilUsuario.Administrador ? "Administrador" : u.Funcao?.ToString()
            ?? throw new RegraNegocioException(403, "Funcionário sem função atribuída.");
        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, papel),
            new("perfil", u.Perfil.ToString()),
        };
        if (u.Funcao is not null) claims.Add(new Claim("funcao", u.Funcao.ToString()!));

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var credenciais = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256);
        var expira = DateTime.UtcNow.AddHours(4);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            claims, expires: expira, signingCredentials: credenciais);
        return new { token = new JwtSecurityTokenHandler().WriteToken(token),
            tipo = "Bearer", expiraEmUtc = expira, usuario = UsuarioResposta.De(u) };
    }
}
