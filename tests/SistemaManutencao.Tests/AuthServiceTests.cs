using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.DTOs;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;
using SistemaManutencao.API.Services;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Testes de caracterização do AuthService com PasswordHasher real e
/// IConfiguration in-memory (Jwt:Key/Issuer/Audience). O service devolve um
/// tipo anônimo; as propriedades são lidas por reflexão em Valor().
/// </summary>
public class AuthServiceTests
{
    private const string Senha = "senha-teste-123";

    private static AuthService CriarAuthService(AppDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "chave-somente-para-testes-0123456789abcdef",
            ["Jwt:Issuer"] = "pim-testes",
            ["Jwt:Audience"] = "pim-testes"
        }).Build();
        return new AuthService(db, new PasswordHasher<Usuario>(), config);
    }

    private static Task<object> LoginAsync(BancoTeste banco, string email, string senha = Senha)
        => banco.OpAsync(db => CriarAuthService(db).LoginAsync(new LoginDto { Email = email, Senha = senha }));

    private static async Task<Usuario> SemearUsuarioAsync(BancoTeste banco, Usuario usuario)
    {
        var hasher = new PasswordHasher<Usuario>();
        usuario.SenhaHash = hasher.HashPassword(usuario, Senha);
        await banco.OpAsync(db => { db.Usuarios.Add(usuario); return db.SaveChangesAsync(); });
        return usuario;
    }

    private static object Valor(object anonimo, string propriedade)
        => anonimo.GetType().GetProperty(propriedade)!.GetValue(anonimo)!;

    [Fact]
    public async Task LoginValidoRetornaTokenBearerEUsuario()
    {
        await using var banco = await BancoTeste.CriarAsync();
        var semeado = await SemearUsuarioAsync(banco, new Usuario { Nome = "Tecnico Silva",
            Email = "tecnico.silva@pim.local", Perfil = PerfilUsuario.Funcionario,
            Funcao = FuncaoFuncionario.Tecnico });

        var resultado = await LoginAsync(banco, "tecnico.silva@pim.local");

        var token = Assert.IsType<string>(Valor(resultado, "token"));
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length); // formato JWT (header.payload.signature)
        Assert.Equal("Bearer", Assert.IsType<string>(Valor(resultado, "tipo")));
        var expiraEmUtc = Assert.IsType<DateTime>(Valor(resultado, "expiraEmUtc"));
        Assert.True(expiraEmUtc > DateTime.UtcNow);

        var usuario = Assert.IsType<UsuarioResposta>(Valor(resultado, "usuario"));
        Assert.Equal(semeado.Id, usuario.Id);
        Assert.Equal("Tecnico Silva", usuario.Nome);
        Assert.Equal(PerfilUsuario.Funcionario, usuario.Perfil);
        Assert.Equal(FuncaoFuncionario.Tecnico, usuario.Funcao);
        Assert.True(usuario.Ativo);
    }

    [Fact]
    public async Task LoginComSenhaErradaLanca401()
    {
        await using var banco = await BancoTeste.CriarAsync();
        await SemearUsuarioAsync(banco, new Usuario { Nome = "Tecnico Silva",
            Email = "tecnico.silva@pim.local", Perfil = PerfilUsuario.Funcionario,
            Funcao = FuncaoFuncionario.Tecnico });

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(
            () => LoginAsync(banco, "tecnico.silva@pim.local", "senha-completamente-errada"));
        Assert.Equal(401, erro.StatusHttp);
        Assert.Equal("Email ou senha inválidos.", erro.Message);
    }

    [Fact]
    public async Task LoginComUsuarioInativoLanca401()
    {
        // Comportamento atual: usuário inativo é indistinguível de inexistente (401).
        await using var banco = await BancoTeste.CriarAsync();
        await SemearUsuarioAsync(banco, new Usuario { Nome = "Desligado", Email = "desligado@pim.local",
            Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Operador, Ativo = false });

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => LoginAsync(banco, "desligado@pim.local"));
        Assert.Equal(401, erro.StatusHttp);
        Assert.Equal("Email ou senha inválidos.", erro.Message);
    }

    [Fact]
    public async Task LoginComEmailInexistenteLanca401()
    {
        // Email inexistente: mesmo 401 e mesma mensagem de senha errada. O caminho executa
        // a verificação de hash fictício (tempo de resposta constante anti-enumeração).
        await using var banco = await BancoTeste.CriarAsync();

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(
            () => LoginAsync(banco, "inexistente@pim.local"));

        Assert.Equal(401, erro.StatusHttp);
        Assert.Equal("Email ou senha inválidos.", erro.Message);
    }

    [Fact]
    public async Task LoginDeFuncionarioSemFuncaoLanca403()
    {
        await using var banco = await BancoTeste.CriarAsync();
        await SemearUsuarioAsync(banco, new Usuario { Nome = "Sem Funcao",
            Email = "semfuncao@pim.local", Perfil = PerfilUsuario.Funcionario, Funcao = null });

        var erro = await Assert.ThrowsAsync<RegraNegocioException>(() => LoginAsync(banco, "semfuncao@pim.local"));
        Assert.Equal(403, erro.StatusHttp);
        Assert.Equal("Funcionário sem função atribuída.", erro.Message);
    }

    [Fact]
    public async Task LoginComEmailEmCaixaDiferenteEncontraUsuario()
    {
        await using var banco = await BancoTeste.CriarAsync();
        await SemearUsuarioAsync(banco, new Usuario { Nome = "Operador Tres",
            Email = "operador.tres@pim.local", Perfil = PerfilUsuario.Funcionario,
            Funcao = FuncaoFuncionario.Operador });

        var resultado = await LoginAsync(banco, "Operador.Tres@PIM.Local");

        var usuario = Assert.IsType<UsuarioResposta>(Valor(resultado, "usuario"));
        Assert.Equal("operador.tres@pim.local", usuario.Email);
        Assert.Equal("Operador Tres", usuario.Nome);
    }

    [Fact]
    public async Task AdministradorSemFuncaoPodeLogar()
    {
        // Comportamento atual: Administrador recebe papel fixo "Administrador" e não
        // depende de Funcao (o 403 de "sem função" vale apenas para Funcionario).
        await using var banco = await BancoTeste.CriarAsync();
        await SemearUsuarioAsync(banco, new Usuario { Nome = "Root Admin",
            Email = "admin@pim.local", Perfil = PerfilUsuario.Administrador, Funcao = null });

        var resultado = await LoginAsync(banco, "admin@pim.local");

        var token = Assert.IsType<string>(Valor(resultado, "token"));
        Assert.False(string.IsNullOrWhiteSpace(token));
        var usuario = Assert.IsType<UsuarioResposta>(Valor(resultado, "usuario"));
        Assert.Equal(PerfilUsuario.Administrador, usuario.Perfil);
        Assert.Null(usuario.Funcao);
    }
}
