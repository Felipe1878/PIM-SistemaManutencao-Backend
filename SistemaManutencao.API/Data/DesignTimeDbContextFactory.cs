using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SistemaManutencao.API.Data;

/// <summary>
/// Fábrica de design-time usada SOMENTE pelas ferramentas do EF Core (dotnet ef).
/// Decupla o tooling do bootstrap da API: Program.cs exige "Jwt:Key", que não existe
/// em tempo de design, o que impedia criar/remover migrations. Não é usada em runtime.
/// Lê "ConnectionStrings:DefaultConnection" do appsettings.json (mesma string LocalDB
/// já versionada); se o arquivo não for encontrado, cai no mesmo fallback LocalDB.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackLocalDB =
        "Server=(localdb)\\MSSQLLocalDB;Database=SistemaManutencaoPIM;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        // Em design-time a factory roda a partir do bin do projeto (bin/Debug/net8.0).
        var raizProjeto = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var candidatos = new[]
        {
            Path.Combine(raizProjeto, "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "SistemaManutencao.API", "appsettings.json")
        };

        string? conexao = null;
        foreach (var caminho in candidatos.Where(File.Exists))
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(caminho)!)
                .AddJsonFile(Path.GetFileName(caminho), optional: true)
                .Build();
            conexao = config.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrWhiteSpace(conexao)) break;
        }

        conexao ??= FallbackLocalDB;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conexao)
            .Options;

        return new AppDbContext(options);
    }
}
