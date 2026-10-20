using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.Data;

public static class DbInitializer
{
    public static async Task CriarAdministradorInicialAsync(
        AppDbContext db, IConfiguration config, IPasswordHasher<Usuario> hasher)
    {
        if (await db.Usuarios.AnyAsync()) return;
        var email = config["SeedAdmin:Email"]?.Trim().ToLowerInvariant();
        var senha = config["SeedAdmin:Senha"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha)) return;
        if (senha.Length < 8) throw new InvalidOperationException("Senha inicial deve ter no mínimo 8 caracteres.");
        var admin = new Usuario { Nome = "Administrador Inicial", Email = email,
            Perfil = PerfilUsuario.Administrador, Funcao = null, Ativo = true };
        admin.SenhaHash = hasher.HashPassword(admin, senha);
        db.Usuarios.Add(admin);
        await db.SaveChangesAsync();
    }
}
