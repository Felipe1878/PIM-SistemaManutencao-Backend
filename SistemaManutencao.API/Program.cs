using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SistemaManutencao.API.Data;
using SistemaManutencao.API.Helpers;
using SistemaManutencao.API.Models;
using SistemaManutencao.API.Services;

var builder = WebApplication.CreateBuilder(args);
var chave = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(chave) || Encoding.UTF8.GetByteCount(chave) < 32)
    throw new InvalidOperationException("Defina Jwt:Key com pelo menos 32 bytes (veja scripts/iniciar-dev.ps1 ou README).");

builder.Services.AddControllers().AddJsonOptions(x =>
    x.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<AppDbContext>(x =>
    x.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OcorrenciaService>();
builder.Services.AddScoped<OrdemServicoService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
    // Tokens emitidos incluem ID; a conta é conferida novamente para impedir acesso após inativação.
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idText, out var id)) { context.Fail("Token sem usuário."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var atual = await db.Usuarios.AsNoTracking()
                .Where(x => x.Id == id && x.Ativo)
                .Select(x => new { x.Perfil, x.Funcao }).SingleOrDefaultAsync();
            var papelAtual = atual?.Perfil == SistemaManutencao.API.Enums.PerfilUsuario.Administrador
                ? "Administrador" : atual?.Funcao?.ToString();
            if (atual is null || context.Principal?.FindFirstValue(ClaimTypes.Role) != papelAtual)
                context.Fail("Usuário inativo ou permissões alteradas. Faça login novamente.");
        },
        // 401/403 passam a ter corpo {erro} no mesmo envelope dos demais erros de negócio (antes: corpo vazio).
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new
                { erro = "Autenticação necessária: token ausente, inválido ou expirado. Faça login novamente." });
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { erro = "Você não possui permissão para realizar esta ação." });
        }
    };
});
builder.Services.AddAuthorization();
// CORS para o React no browser (Flutter não é afetado — CORS é mecanismo de browser).
// Origens permitidas vêm de "Cors:Origens" na configuração; sem origens configuradas, nada é liberado.
var origensCors = builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("Frontends",
    p => p.WithOrigins(origensCors).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sistema de Manutenção - PIM", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT obtido em POST /api/auth/login. Digite apenas o token.",
        Name = "Authorization", In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();
// Erros de negócio previsíveis recebem HTTP 400/403/404/409 sem mostrar detalhes internos.
app.Use(async (http, next) =>
{
    try { await next(); }
    catch (RegraNegocioException ex)
    {
        if (http.Response.HasStarted) throw;
        http.Response.StatusCode = ex.StatusHttp;
        await http.Response.WriteAsJsonAsync(new { erro = ex.Message });
    }
    catch (DbUpdateException ex)
    {
        if (http.Response.HasStarted) throw;
        app.Logger.LogWarning(ex, "Falha de integridade ao salvar banco");
        http.Response.StatusCode = 409;
        await http.Response.WriteAsJsonAsync(new { erro = "Conflito de dados: registro duplicado ou referência inválida." });
    }
    catch (Exception ex)
    {
        if (http.Response.HasStarted) throw;
        app.Logger.LogError(ex, "Erro interno não tratado");
        http.Response.StatusCode = 500;
        await http.Response.WriteAsJsonAsync(new { erro = "Erro interno do servidor." });
    }
});
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(); app.UseSwaggerUI();
    if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();
        await DbInitializer.CriarAdministradorInicialAsync(db, builder.Configuration, hasher);
    }
}
// CORS antes da autenticação para que requisições preflight (OPTIONS) não sejam bloqueadas.
app.UseCors("Frontends");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "online", servico = "SistemaManutencao.API" }));
app.Run();

// Permite criar testes de integração via WebApplicationFactory se necessário.
public partial class Program { }
