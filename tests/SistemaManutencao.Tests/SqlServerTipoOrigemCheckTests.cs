using Microsoft.EntityFrameworkCore;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Models;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Testes de integração da CHECK <c>CK_OrdensServicos_TipoOrigem</c> contra SQL Server REAL (LocalDB).
/// Validam migrations desde zero, estrutura (7 tabelas, 8 FKs, índices únicos) e inserts diretos no
/// SGBD — comprovando a constraint no motor SQL Server, e não apenas no provedor SQLite.
/// Executam EXCLUSIVAMENTE no banco descartável <c>SistemaManutencaoPIM_ValidationTemp</c>; o banco
/// oficial <c>SistemaManutencaoPIM</c> NUNCA é referenciado (guarda em <see cref="DroparBancoTemporario"/>).
/// </summary>
[Trait("Categoria", "SqlServer")]
public sealed class SqlServerTipoOrigemCheckTests : IAsyncLifetime
{
    private const string NomeBancoTemp = "SistemaManutencaoPIM_ValidationTemp";
    private const string ConexaoTemp =
        @"Server=(localdb)\MSSQLLocalDB;Database=SistemaManutencaoPIM_ValidationTemp;Trusted_Connection=True;TrustServerCertificate=True";
    private const string ConexaoMaster =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private AppDbContext _db = null!;

    // IDs gerados pelo identity durante o seed deste teste (nunca fixos no código).
    private int _ocorrenciaValida;   // ocorrência Aberta usada pelo insert corretivo válido
    private int _ocorrenciaLivre;    // ocorrência Aberta reservada para os casos inválidos
    private int _planoValido;        // plano usado pelo insert preventivo válido
    private int _planoLivre;         // plano reservado para os casos inválidos

    public async Task InitializeAsync()
    {
        await DroparBancoTemporario();
        _db = CriarContexto();
        // Pipeline completo de migrations desde zero: InitialCreate + OrdemServicoTipoOrigemCheck.
        await _db.Database.MigrateAsync();
        await SemeiaDadosMinimosAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
            await _db.DisposeAsync();
        await DroparBancoTemporario();
    }

    // ---------------------------------------------------------------- estrutura

    [Fact]
    public async Task Estrutura_SeteTabelasOitoFks_ECheckAtiva()
    {
        var tabelas = await EscalarAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'Usuarios',N'Setores',N'Equipamentos'," +
            "N'Ocorrencias',N'OrdensServicos',N'Manutencoes',N'PlanosManutencao')");
        Assert.Equal(7, tabelas);

        var fks = await EscalarAsync("SELECT COUNT(*) FROM sys.foreign_keys");
        Assert.Equal(8, fks);

        var checkAtiva = await EscalarAsync(
            "SELECT COUNT(*) FROM sys.check_constraints " +
            "WHERE name = N'CK_OrdensServicos_TipoOrigem' " +
            "AND parent_object_id = OBJECT_ID(N'dbo.OrdensServicos') " +
            "AND is_disabled = 0 AND is_not_trusted = 0");
        Assert.Equal(1, checkAtiva);
    }

    [Fact]
    public async Task Estrutura_IndicesUnicos_PresentesComFiltros()
    {
        var esperados = new (string Nome, string Tabela)[]
        {
            ("IX_Usuarios_Email", "Usuarios"),
            ("IX_Equipamentos_Patrimonio", "Equipamentos"),
            ("IX_Setores_Nome", "Setores"),
            ("IX_Ocorrencias_EquipamentoId", "Ocorrencias"),
            ("IX_OrdensServicos_OcorrenciaId", "OrdensServicos"),
            ("IX_Manutencoes_OrdemServicoId", "Manutencoes"),
        };

        foreach (var (nome, tabela) in esperados)
        {
            var existe = await EscalarAsync(
                "SELECT COUNT(*) FROM sys.indexes " +
                "WHERE name = N'" + nome + "' " +
                "AND object_id = OBJECT_ID(N'dbo." + tabela + "') AND is_unique = 1");
            Assert.True(existe == 1, $"Índice único ausente: {nome}");
        }

        // SQL Server normaliza filtros (espaços/parênteses extras) — compara sem espaços nem parênteses.
        static string Normalizado(string? s) => (s ?? string.Empty)
            .Replace(" ", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);

        var filtroSetores = await TextoAsync(
            "SELECT filter_definition FROM sys.indexes WHERE name = N'IX_Setores_Nome'");
        Assert.Equal(Normalizado("[Ativo] = 1"), Normalizado(filtroSetores));

        var filtroOcorrencias = await TextoAsync(
            "SELECT filter_definition FROM sys.indexes WHERE name = N'IX_Ocorrencias_EquipamentoId'");
        Assert.Equal(Normalizado("[Status] <> 2"), Normalizado(filtroOcorrencias));

        var filtroOs = await TextoAsync(
            "SELECT filter_definition FROM sys.indexes WHERE name = N'IX_OrdensServicos_OcorrenciaId'");
        Assert.Equal(Normalizado("[OcorrenciaId] IS NOT NULL"), Normalizado(filtroOs));
    }

    // ------------------------------------------------- inserts válidos (SQL direto)

    [Fact]
    public async Task InsertCorretivaComOcorrencia_E_Aceito()
    {
        await InserirOrdemServicoAsync(ocorrenciaId: _ocorrenciaValida, planoId: null, TipoManutencao.Corretiva);
        var count = await EscalarAsync(
            "SELECT COUNT(*) FROM OrdensServicos WHERE OcorrenciaId = " + _ocorrenciaValida);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InsertPreventivaComPlano_E_Aceito()
    {
        await InserirOrdemServicoAsync(ocorrenciaId: null, planoId: _planoValido, TipoManutencao.Preventiva);
        var count = await EscalarAsync(
            "SELECT COUNT(*) FROM OrdensServicos WHERE PlanoManutencaoId = " + _planoValido);
        Assert.Equal(1, count);
    }

    // --------------------------------------------- inserts inválidos (violam a CHECK)

    [Fact]
    public async Task InsertCorretivaSemOcorrencia_E_RejeitadoPelaCheck()
    {
        var ex = await Record.ExceptionAsync(() =>
            InserirOrdemServicoAsync(ocorrenciaId: null, planoId: null, TipoManutencao.Corretiva));
        Assert.NotNull(ex);
        Assert.Contains("CK_OrdensServicos_TipoOrigem", ex.ToString());
    }

    [Fact]
    public async Task InsertCorretivaComOcorrenciaEPlano_E_RejeitadoPelaCheck()
    {
        var ex = await Record.ExceptionAsync(() =>
            InserirOrdemServicoAsync(ocorrenciaId: _ocorrenciaLivre, planoId: _planoLivre, TipoManutencao.Corretiva));
        Assert.NotNull(ex);
        Assert.Contains("CK_OrdensServicos_TipoOrigem", ex.ToString());
    }

    [Fact]
    public async Task InsertPreventivaSemPlano_E_RejeitadoPelaCheck()
    {
        var ex = await Record.ExceptionAsync(() =>
            InserirOrdemServicoAsync(ocorrenciaId: null, planoId: null, TipoManutencao.Preventiva));
        Assert.NotNull(ex);
        Assert.Contains("CK_OrdensServicos_TipoOrigem", ex.ToString());
    }

    [Fact]
    public async Task InsertPreventivaComOcorrenciaEPlano_E_RejeitadoPelaCheck()
    {
        var ex = await Record.ExceptionAsync(() =>
            InserirOrdemServicoAsync(ocorrenciaId: _ocorrenciaLivre, planoId: _planoLivre, TipoManutencao.Preventiva));
        Assert.NotNull(ex);
        Assert.Contains("CK_OrdensServicos_TipoOrigem", ex.ToString());
    }

    // ------------------------------------------------------------------ helpers

    private static AppDbContext CriarContexto()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConexaoTemp).Options);

    /// <summary>
    /// Dropa APENAS o banco descartável de validação. Guarda obrigatória: se o nome-alvo
    /// deixar de ser o temporário, o método se recusa a executar e o teste falha.
    /// </summary>
    private static async Task DroparBancoTemporario()
    {
        if (!NomeBancoTemp.StartsWith("SistemaManutencaoPIM_", StringComparison.Ordinal) ||
            NomeBancoTemp == "SistemaManutencaoPIM")
        {
            throw new InvalidOperationException("Guarda de segurança: banco-alvo inválido para drop.");
        }

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConexaoMaster).Options);
        await db.Database.ExecuteSqlRawAsync(
            "IF DB_ID(N'" + NomeBancoTemp + "') IS NOT NULL " +
            "BEGIN ALTER DATABASE [" + NomeBancoTemp + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            "DROP DATABASE [" + NomeBancoTemp + "]; END");
    }

    /// <summary>Seed sem Ids explícitos (identity do SQL Server) — captura os IDs gerados.</summary>
    private async Task SemeiaDadosMinimosAsync()
    {
        var setor1 = new Setor { Nome = "Usinagem" };
        var setor2 = new Setor { Nome = "Montagem" };
        var operador = new Usuario { Nome = "Operador", Email = "oper@valid.local", SenhaHash = "teste",
            Perfil = PerfilUsuario.Funcionario, Funcao = FuncaoFuncionario.Operador };
        _db.AddRange(setor1, setor2, operador);
        await _db.SaveChangesAsync();

        var eq1 = new Equipamento { Nome = "Prensa", Patrimonio = "P-VALID-01", SetorId = setor1.Id };
        var eq2 = new Equipamento { Nome = "Torno", Patrimonio = "P-VALID-02", SetorId = setor2.Id };
        var eq3 = new Equipamento { Nome = "Fresadora", Patrimonio = "P-VALID-03", SetorId = setor1.Id };
        var eq4 = new Equipamento { Nome = "Robo", Patrimonio = "P-VALID-04", SetorId = setor2.Id };
        _db.AddRange(eq1, eq2, eq3, eq4);
        await _db.SaveChangesAsync();

        var ocorrValida = new Ocorrencia { EquipamentoId = eq1.Id, UsuarioId = operador.Id,
            Descricao = "Vazamento hidraulico", Prioridade = PrioridadeOcorrencia.Media,
            Status = StatusOcorrencia.Aberta, DataRegistro = DateTime.UtcNow };
        var ocorrLivre = new Ocorrencia { EquipamentoId = eq3.Id, UsuarioId = operador.Id,
            Descricao = "Ruido no fuso", Prioridade = PrioridadeOcorrencia.Baixa,
            Status = StatusOcorrencia.Aberta, DataRegistro = DateTime.UtcNow };
        var planoValido = new PlanoManutencao { EquipamentoId = eq2.Id, Descricao = "Lubrificacao mensal",
            PeriodicidadeDias = 30, ProximaExecucao = DateTime.UtcNow.AddDays(30) };
        var planoLivre = new PlanoManutencao { EquipamentoId = eq4.Id, Descricao = "Calibracao trimestral",
            PeriodicidadeDias = 90, ProximaExecucao = DateTime.UtcNow.AddDays(90) };
        _db.AddRange(ocorrValida, ocorrLivre, planoValido, planoLivre);
        await _db.SaveChangesAsync();

        _ocorrenciaValida = ocorrValida.Id;
        _ocorrenciaLivre = ocorrLivre.Id;
        _planoValido = planoValido.Id;
        _planoLivre = planoLivre.Id;
    }

    /// <summary>INSERT direto no SQL Server, contornando EF e services — prova a constraint no SGBD.</summary>
    private Task InserirOrdemServicoAsync(int? ocorrenciaId, int? planoId, TipoManutencao tipo)
        => _db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO OrdensServicos (OcorrenciaId, PlanoManutencaoId, TecnicoId, TipoManutencao, Status, DataAbertura)
               VALUES ({ocorrenciaId}, {planoId}, NULL, {(int)tipo}, 0, SYSUTCDATETIME())");

    private async Task<int> EscalarAsync(string sql)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<string> TextoAsync(string sql)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
