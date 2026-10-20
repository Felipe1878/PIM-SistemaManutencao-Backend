using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.Tests;

/// <summary>
/// Harness de testes: uma única conexão SQLite ":memory:" aberta por teste, com
/// <c>EnsureCreatedAsync</c> executado UMA vez, e um AppDbContext NOVO para CADA
/// operação de serviço sobre a MESMA conexão — espelha o ciclo scoped-por-request
/// da API em produção.
/// Necessário porque <c>OrdemServicoService.AssumirAsync</c> usa ExecuteUpdateAsync,
/// que atualiza apenas o banco; com um contexto compartilhado, a query de rastreamento
/// em IniciarAsync devolvia a instância STALE do change tracker (TecnicoId nulo),
/// causando 403 falsos (causa raiz comprovada na Missão 03).
/// </summary>
public sealed class BancoTeste : IAsyncDisposable
{
    private readonly SqliteConnection _conexao;

    private BancoTeste(SqliteConnection conexao) => _conexao = conexao;

    public static async Task<BancoTeste> CriarAsync()
    {
        var conexao = new SqliteConnection("DataSource=:memory:");
        await conexao.OpenAsync();
        await using (var db = CriarContexto(conexao))
        {
            await db.Database.EnsureCreatedAsync();
            db.Setores.Add(new Setor { Id = 1, Nome = "Producao" });
            db.Usuarios.AddRange(
                new Usuario { Id = 1, Nome = "Operador", Email = "oper@pim.local", SenhaHash = "teste",
                    Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Operador },
                new Usuario { Id = 2, Nome = "Tecnico", Email = "tec@pim.local", SenhaHash = "teste",
                    Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Tecnico });
            db.Equipamentos.Add(new Equipamento { Id = 1, Nome = "Prensa", Patrimonio = "P01", SetorId = 1 });
            await db.SaveChangesAsync();
        }
        return new BancoTeste(conexao);
    }

    /// <summary>Contexto NOVO sobre a mesma conexão; o chamador é responsável pelo dispose.</summary>
    public AppDbContext NovoContexto() => CriarContexto(_conexao);

    /// <summary>Executa uma operação em um contexto novo (como um request scoped) e o descarta.</summary>
    public async Task<T> OpAsync<T>(Func<AppDbContext, Task<T>> operacao)
    {
        await using var db = NovoContexto();
        return await operacao(db);
    }

    public async Task OpAsync(Func<AppDbContext, Task> operacao)
    {
        await using var db = NovoContexto();
        await operacao(db);
    }

    public async ValueTask DisposeAsync() => await _conexao.DisposeAsync();

    private static AppDbContext CriarContexto(SqliteConnection conexao)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexao).Options);
}
