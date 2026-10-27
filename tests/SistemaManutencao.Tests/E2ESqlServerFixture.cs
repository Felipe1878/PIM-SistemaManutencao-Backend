using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.Enums;
using Xunit;

namespace SistemaManutencao.Tests;

/// <summary>
/// Fixture E2E com SQL Server LocalDB real, migrations e seed pelo startup da API.
/// Cada instância possui banco SistemaManutencaoPIM_E2E_&lt;GUID&gt; exclusivo.
/// CREATE nunca adota/apaga base preexistente. Cleanup exige ownership adquirido no CREATE,
/// nome validado e identidade do catálogo (database_id/create_date), sempre via master.
/// Falhas de startup também executam cleanup; o banco oficial nunca é alvo.
/// </summary>
public sealed class E2ESqlServerFixture : IAsyncLifetime
{
    public const string NomeCollection = "e2e-sqlserver";

    private const string PrefixoBanco = "SistemaManutencaoPIM_E2E_";
    private readonly string NomeBancoTemp = PrefixoBanco + Guid.NewGuid().ToString("N");
    private bool _bancoCriado;
    private int _databaseId;
    private DateTime _createDate;

    public const string EmailAdmin = "admin-e2e@pim.local";
    public const string EmailOperador = "operador-e2e@pim.local";
    public const string EmailTecnico = "tecnico-e2e@pim.local";
    public const string NomeOperador = "Operador E2E";
    public const string NomeTecnico = "Tecnico E2E";
    public const string SenhaUsuarios = "SenhaE2E-Teste123!";

    /// <summary>Chave JWT fictícia de 48 bytes exigida por Program.cs — só existe no processo de teste.</summary>
    private const string ChaveJwtFicticia = "e2e-chave-ficticia-de-48-chars-0123456789abcdefg";

    private string ConexaoTemp =>
        @"Server=(localdb)\MSSQLLocalDB;Database=" + NomeBancoTemp + ";Trusted_Connection=True;TrustServerCertificate=True";
    private const string ConexaoMaster =
        @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly Dictionary<string, HttpClient> _clientesAutenticados = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        // Contrato interno: o Program.cs exige chave JWT com >= 32 bytes (o valor 48 aqui é
        // apenas o tamanho adotado por esta fixture); a chave é fictícia e existe só no processo.
        Assert.Equal(48, Encoding.UTF8.GetByteCount(ChaveJwtFicticia));

        ValidarNomeBanco();
        try
        {
            await CriarBancoTemporarioAsync();

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                // Ambiente Development é obrigatório: o bloco de migrations/seed do Program.cs
                // só executa quando app.Environment.IsDevelopment().
                b.UseEnvironment("Development");
                b.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                b.UseSetting("Jwt:Key", ChaveJwtFicticia);
                // O Program.cs aplica as 2 migrations no banco recém-criado e semeia o admin.
                b.UseSetting("Database:ApplyMigrationsOnStartup", "true");
                b.UseSetting("SeedAdmin:Email", EmailAdmin);
                b.UseSetting("SeedAdmin:Senha", SenhaUsuarios);
                // Defesa em profundidade: até a config de conexão aponta ao temporário — nunca ao oficial.
                b.UseSetting("ConnectionStrings:DefaultConnection", ConexaoTemp);
                b.ConfigureServices(services =>
                {
                    // Remove TODOS os registros do AppDbContext feitos em Program.cs (no EF Core 8 a
                    // action UseSqlServer é capturada pelo descritor de DbContextOptions<T>) e
                    // re-registra o contexto sobre o SQL Server LocalDB do banco temporário.
                    services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
                    services.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConexaoTemp));
                });
            });

            // Warm-up: criar o cliente constrói o host (dispara MigrateAsync + seed). O login do
            // admin semeado comprova ponta a ponta: migrations, JWT e seed — falhas estouram aqui,
            // com mensagem clara, antes de qualquer teste.
            var cliente = _factory.CreateClient();
            var saude = await cliente.GetAsync("/api/health");
            Assert.True(saude.IsSuccessStatusCode,
                $"API não respondeu ao health check no startup E2E: {(int)saude.StatusCode}.");
            var login = await cliente.PostAsJsonAsync("/api/auth/login",
                new { email = EmailAdmin, senha = SenhaUsuarios });
            Assert.True(login.IsSuccessStatusCode,
                $"Login do admin semeado falhou (migrations/seed/JWT): {(int)login.StatusCode} " +
                $"{await login.Content.ReadAsStringAsync()}.");

            // Defesa em profundidade: confirma que o DbContext EFETIVAMENTE em uso aponta para o
            // banco descartável. Se o re-registro do AppDbContext deixasse de prevalecer, o startup
            // (MigrateAsync + seed) teria escrito no banco oficial — esta verificação aborta a
            // fixture antes de qualquer teste, com o nome real do banco na mensagem.
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var nomeEfetivo = db.Database.GetDbConnection().Database;
                Assert.Equal(NomeBancoTemp, nomeEfetivo);
            }
        }
        catch (Exception inicializacao)
        {
            try { await DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(inicializacao, cleanup); }
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            foreach (var cliente in _clientesAutenticados.Values) cliente.Dispose();
            _clientesAutenticados.Clear();
            if (_factory is not null) await _factory.DisposeAsync();
        }
        finally
        {
            _factory = null!;
            await DroparBancoTemporarioAsync();
        }
    }

    // -------------------------------------------------------------------- clientes

    /// <summary>Cliente sem token (health, login e casos 401).</summary>
    public HttpClient ClienteAnonimo() => _factory.CreateClient();

    /// <summary>Admin semeado pelo startup da própria API.</summary>
    public Task<HttpClient> AdminAsync() => EntrarAsync(EmailAdmin);

    /// <summary>Operador da suíte E2E (criado via API se ainda não existir).</summary>
    public Task<HttpClient> OperadorAsync()
        => GarantirUsuarioAsync(EmailOperador, NomeOperador, FuncaoFuncionario.Operador);

    /// <summary>Técnico da suíte E2E (criado via API se ainda não existir).</summary>
    public Task<HttpClient> TecnicoAsync()
        => GarantirUsuarioAsync(EmailTecnico, NomeTecnico, FuncaoFuncionario.Tecnico);

    /// <summary>Login real (POST /api/auth/login) com cache do cliente Bearer por usuário.</summary>
    private async Task<HttpClient> EntrarAsync(string email)
    {
        if (_clientesAutenticados.TryGetValue(email, out var cacheado)) return cacheado;
        var cliente = _factory.CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/api/auth/login", new { email, senha = SenhaUsuarios });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Login E2E falhou para {email}: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
        var token = (await resposta.CorpoAsync()).GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token), "Login E2E devolveu token vazio.");
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _clientesAutenticados[email] = cliente;
        return cliente;
    }

    /// <summary>
    /// Garante um usuário Funcionario: tenta logar; se a conta ainda não existe, o Admin a cria
    /// via POST /api/usuarios (409 — já existente — também é aceito) e refaz o login.
    /// Torna cada classe de teste independente da ordem de execução dentro da coleção E2E.
    /// </summary>
    private async Task<HttpClient> GarantirUsuarioAsync(string email, string nome, FuncaoFuncionario funcao)
    {
        if (_clientesAutenticados.TryGetValue(email, out var cacheado)) return cacheado;
        var tentativa = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { email, senha = SenhaUsuarios });
        if (!tentativa.IsSuccessStatusCode)
        {
            var admin = await AdminAsync();
            var criacao = await admin.PostAsJsonAsync("/api/usuarios",
                new { nome, email, senha = SenhaUsuarios, perfil = "Funcionario", funcao = funcao.ToString() });
            Assert.True(criacao.IsSuccessStatusCode || criacao.StatusCode == HttpStatusCode.Conflict,
                $"Criação do usuário {email} falhou: {(int)criacao.StatusCode} {await criacao.Content.ReadAsStringAsync()}.");
            if (criacao.StatusCode == HttpStatusCode.Conflict)
            {
                // 409 aqui significa que a conta JÁ existia — então o login inicial falhou porque
                // a senha existente difere de SenhaUsuarios (conta inativa ou senha divergente).
                // Falhar aqui com diagnóstico explícito evita o Assert opaco de login mais abaixo.
                var retry = await _factory.CreateClient()
                    .PostAsJsonAsync("/api/auth/login", new { email, senha = SenhaUsuarios });
                Assert.True(retry.IsSuccessStatusCode,
                    $"Usuário {email} já existia (409) e o login com a senha da fixture continua falhando " +
                    $"({(int)retry.StatusCode}). O banco de teste exige que o usuário seja criado com " +
                    $"a senha da fixture; senha divergente ou conta inativa quebra o cenário E2E.");
            }
        }
        return await EntrarAsync(email);
    }

    /// <summary>
    /// Cria usuário via POST /api/usuarios (Admin), como faria o frontend. 409 é aceito para
    /// idempotência entre classes que compartilham o mesmo banco temporário.
    /// </summary>
    public async Task RegistrarUsuarioAsync(string nome, string email, string funcao)
    {
        var admin = await AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/usuarios",
            new { nome, email, senha = SenhaUsuarios, perfil = "Funcionario", funcao });
        Assert.True(resposta.IsSuccessStatusCode || resposta.StatusCode == HttpStatusCode.Conflict,
            $"Registrar usuário {email} falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
    }

    // ------------------------------------------------------------ cenários via API

    public async Task<int> CriarSetorAsync(string nome)
    {
        var admin = await AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/setores", new { nome });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar setor E2E falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    public async Task<int> CriarEquipamentoAsync(int setorId, string nome, string patrimonio,
        string fabricante = "Fabricante E2E", string modelo = "Modelo E2E", string numeroSerie = "NS-E2E")
    {
        var admin = await AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/equipamentos",
            new { nome, patrimonio, fabricante, modelo, numeroSerie, setorId });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar equipamento E2E falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    public async Task<int> CriarOcorrenciaAsync(int equipamentoId, string descricao, string prioridade = "Alta")
    {
        var operador = await OperadorAsync();
        var resposta = await operador.PostAsJsonAsync("/api/ocorrencias",
            new { equipamentoId, descricao, prioridade });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar ocorrência E2E falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    public async Task<int> CriarPlanoAsync(int equipamentoId, string descricao,
        int periodicidadeDias, DateTime proximaExecucao)
    {
        var admin = await AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/planos-manutencao",
            new { equipamentoId, descricao, periodicidadeDias, proximaExecucao });
        Assert.True(resposta.IsSuccessStatusCode,
            $"Criar plano E2E falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
        return (await resposta.CorpoAsync()).GetProperty("id").GetInt32();
    }

    // -------------------------------------------------------------------- helpers

    /// <summary>GET com asserção de sucesso; devolve o corpo JSON.</summary>
    public static async Task<JsonElement> GetCorpoAsync(HttpClient cliente, string url)
    {
        var resposta = await cliente.GetAsync(url);
        Assert.True(resposta.IsSuccessStatusCode,
            $"GET {url} falhou: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync()}.");
        return await resposta.CorpoAsync();
    }

    /// <summary>
    /// Converte um timestamp JSON em instante UTC com tolerância. O contrato define timestamps UTC
    /// (sufixo Z), porém o GET de planos devolve datas lidas do SQL Server com Kind=Unspecified
    /// (sem sufixo Z); nessas o valor é interpretado como UTC, igual ao restante do sistema
    /// (os endpoints de OS usam ComoUtc para marcar a mesma informação como UTC).
    /// </summary>
    public static DateTimeOffset ParseInstante(string? texto)
    {
        Assert.False(string.IsNullOrWhiteSpace(texto), "Timestamp ausente no corpo JSON.");
        if (!(texto.EndsWith("Z", StringComparison.Ordinal) || texto.EndsWith("z", StringComparison.Ordinal)))
            texto += "Z";
        return DateTimeOffset.Parse(texto);
    }

    private void ValidarNomeBanco()
    {
        if (!NomeBancoTemp.StartsWith(PrefixoBanco, StringComparison.Ordinal) ||
            !Guid.TryParseExact(NomeBancoTemp[PrefixoBanco.Length..], "N", out _) ||
            new SqlConnectionStringBuilder(ConexaoTemp).InitialCatalog != NomeBancoTemp ||
            new SqlConnectionStringBuilder(ConexaoMaster).InitialCatalog != "master")
            throw new InvalidOperationException("Guarda E2E: nome/conexão não autorizado.");
    }

    private async Task CriarBancoTemporarioAsync()
    {
        ValidarNomeBanco();
        await using var conexao = new SqlConnection(ConexaoMaster);
        await conexao.OpenAsync();
        await using var comando = conexao.CreateCommand();
        // CREATE falha se já existir. Não há drop inicial nem adoção de banco preexistente.
        comando.CommandText = $"CREATE DATABASE [{NomeBancoTemp}]";
        await comando.ExecuteNonQueryAsync();
        // Ownership só é adquirido após CREATE bem-sucedido, nunca após mera existência.
        _bancoCriado = true;
        comando.CommandText = "SELECT database_id, create_date FROM sys.databases WHERE name = @nome";
        comando.Parameters.AddWithValue("@nome", NomeBancoTemp);
        await using var leitor = await comando.ExecuteReaderAsync();
        if (!await leitor.ReadAsync()) throw new InvalidOperationException("Banco E2E criado não localizado.");
        _databaseId = leitor.GetInt32(0);
        _createDate = leitor.GetDateTime(1);
    }

    private async Task DroparBancoTemporarioAsync()
    {
        if (!_bancoCriado) return; // Dispose antes de Initialize, CREATE recusado ou cleanup repetido.
        ValidarNomeBanco();
        await using var conexao = new SqlConnection(ConexaoMaster);
        await conexao.OpenAsync();
        await using var comando = conexao.CreateCommand();
        // Nome exclusivo + ownership local + identidade do catálogo. Se a base foi substituída,
        // recusa o drop em vez de apagar uma base criada por outro processo.
        comando.CommandText = $"""
            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @nome)
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.databases
                    WHERE name = @nome AND database_id = @id AND create_date = @criacao)
                    THROW 50001, 'Guarda E2E: ownership do banco não confere; drop recusado.', 1;
                ALTER DATABASE [{NomeBancoTemp}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{NomeBancoTemp}];
            END
            """;
        comando.Parameters.AddWithValue("@nome", NomeBancoTemp);
        comando.Parameters.AddWithValue("@id", _databaseId);
        comando.Parameters.Add("@criacao", System.Data.SqlDbType.DateTime).Value = _createDate;
        await comando.ExecuteNonQueryAsync();
        _bancoCriado = false;
    }
}

/// <summary>Coleção única E2E: um servidor + um banco temporário para todas as classes E2E (execução sequencial).</summary>
[CollectionDefinition(E2ESqlServerFixture.NomeCollection)]
public sealed class E2ESqlServerCollection : ICollectionFixture<E2ESqlServerFixture>
{
}
