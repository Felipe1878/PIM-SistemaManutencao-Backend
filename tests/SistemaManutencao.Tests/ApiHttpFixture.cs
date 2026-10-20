using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Models;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Fixture de integração HTTP: um único servidor in-memory (WebApplicationFactory<Program>)
/// sobre um banco SQLite ":memory:" mantido aberto pela vida da conexão (a coleção
/// "http-integracao" garante execução sequencial das classes de teste que o compartilham).
/// Configuração: Jwt:Key de teste (≥32 bytes), ambiente Development (carrega
/// appsettings.Development.json com Cors:Origens) e Database:ApplyMigrationsOnStartup=false
/// (a API não roda MigrateAsync/DbInitializer; os usuários são semeados aqui com hash REAL).
/// </summary>
public sealed class ApiHttpFixture : IAsyncLifetime
{
    public const string NomeCollection = "http-integracao";
    public const string SenhaPadrao = "SenhaForte123!";
    public const string AdminEmail = "admin@teste.local";
    public const string OperadorEmail = "operador@teste.local";
    public const string TecnicoEmail = "tecnico@teste.local";

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly Dictionary<string, HttpClient> _clientesAutenticados = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        // O banco ":memory:" vive enquanto a conexão estiver aberta; mantê-la aberta
        // é o que faz o mesmo schema persistir entre requests/scopes do servidor.
        await _conexao.OpenAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Key", "chave-de-teste-integracao-com-32-bytes-minimo");
            b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            b.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            b.ConfigureServices(services =>
            {
                // Remove as opções de SQL Server registradas em Program.cs (no EF Core 8
                // a action UseSqlServer é capturada pelo descritor DbContextOptions<T>)
                // e re-registra o DbContext sobre a conexão SQLite da fixture.
                services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conexao));
            });
        });
        await SemearAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task SemearAsync()
    {
        using var escopo = _factory.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        var hasher = escopo.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
        var admin = new Usuario { Nome = "Administrador Teste", Email = AdminEmail,
            Perfil = PerfilUsuario.Administrador, Funcao = null };
        var operador = new Usuario { Nome = "Operador Teste", Email = OperadorEmail,
            Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Operador };
        var tecnico = new Usuario { Nome = "Tecnico Teste", Email = TecnicoEmail,
            Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Tecnico };
        admin.SenhaHash = hasher.HashPassword(admin, SenhaPadrao);
        operador.SenhaHash = hasher.HashPassword(operador, SenhaPadrao);
        tecnico.SenhaHash = hasher.HashPassword(tecnico, SenhaPadrao);
        db.Usuarios.AddRange(admin, operador, tecnico);
        await db.SaveChangesAsync();
    }

    /// <summary>Cliente sem token (para login e casos 401/CORS).</summary>
    public HttpClient ClienteAnonimo() => _factory.CreateClient();

    /// <summary>Faz login real (POST /api/auth/login) e devolve um cliente com Bearer token.</summary>
    public async Task<HttpClient> ClienteAsync(string email, string senha = SenhaPadrao)
    {
        if (senha == SenhaPadrao && _clientesAutenticados.TryGetValue(email, out var cacheado))
            return cacheado;
        var cliente = _factory.CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/api/auth/login", new { email, senha });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Login de fixture falhou para {email}: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}");
        var corpo = await resposta.CorpoAsync();
        var token = corpo.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token), "Login devolveu token vazio.");
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (senha == SenhaPadrao) _clientesAutenticados[email] = cliente;
        return cliente;
    }

    public Task<HttpClient> AdminAsync() => ClienteAsync(AdminEmail);
    public Task<HttpClient> OperadorAsync() => ClienteAsync(OperadorEmail);
    public Task<HttpClient> TecnicoAsync() => ClienteAsync(TecnicoEmail);

    public Task<HttpClient> ClienteDoPerfilAsync(string perfil) => perfil switch
    {
        "admin" => AdminAsync(),
        "operador" => OperadorAsync(),
        "tecnico" => TecnicoAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(perfil), perfil, "Perfil desconhecido.")
    };

    // ---- Helpers de cenário (criação via API real, com nomes/patrimônios únicos por sufixo) ----

    public async Task<int> CriarSetorAsync(string nome)
    {
        var admin = await AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/setores", new { nome });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar setor falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    public async Task<int> CriarEquipamentoAsync(int setorId, string nome, string patrimonio)
    {
        var admin = await AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/equipamentos", new { nome, patrimonio, setorId });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar equipamento falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    public async Task<int> CriarOcorrenciaAsync(int equipamentoId, string descricao, string prioridade = "Alta")
    {
        var operador = await OperadorAsync();
        var resposta = await operador.PostAsJsonAsync("/api/ocorrencias", new { equipamentoId, descricao, prioridade });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar ocorrência falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    /// <summary>Setor + equipamento + ocorrência criados via API, com nomes derivados do sufixo.</summary>
    public async Task<(int SetorId, int EquipamentoId, int OcorrenciaId)> CriarCenarioOcorrenciaAsync(
        string sufixo, string descricao)
    {
        var setorId = await CriarSetorAsync($"Setor {sufixo}");
        var equipamentoId = await CriarEquipamentoAsync(setorId, $"Equipamento {sufixo}", $"PATRIMONIO-{sufixo}");
        var ocorrenciaId = await CriarOcorrenciaAsync(equipamentoId, descricao);
        return (setorId, equipamentoId, ocorrenciaId);
    }
}

/// <summary>Marca as classes de teste HTTP como uma coleção única sequencial (mesmo servidor/banco).</summary>
[CollectionDefinition(NomeCollection)]
public sealed class HttpIntegracaoCollection : ICollectionFixture<ApiHttpFixture>
{
    public const string NomeCollection = ApiHttpFixture.NomeCollection;
}

/// <summary>Extensões de leitura de corpos JSON das respostas HTTP.</summary>
public static class HttpRespostaExtensao
{
    public static async Task<JsonElement> CorpoAsync(this HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(corpo), $"Resposta sem corpo ({(int)resposta.StatusCode}).");
        return JsonSerializer.Deserialize<JsonElement>(corpo);
    }

    /// <summary>Lê o envelope de erro {"erro": "..."} usado pela API (inclusive em 401/403 do middleware).</summary>
    public static async Task<string> ErroAsync(this HttpResponseMessage resposta)
    {
        var corpo = await resposta.CorpoAsync();
        Assert.True(corpo.ValueKind == JsonValueKind.Object && corpo.TryGetProperty("erro", out _),
            $"Corpo sem propriedade 'erro': {corpo}");
        return corpo.GetProperty("erro").GetString()!;
    }
}
