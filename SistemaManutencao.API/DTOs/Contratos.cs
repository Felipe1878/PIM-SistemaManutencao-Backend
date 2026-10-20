using System.ComponentModel.DataAnnotations;
using SistemaManutencao.API.Enums;
using SistemaManutencao.API.Models;

namespace SistemaManutencao.API.DTOs;

public class LoginDto
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Senha { get; set; } = "";
}
public class CriarUsuarioDto
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nome { get; set; } = "";
    [Required, EmailAddress, StringLength(180)] public string Email { get; set; } = "";
    [Required, MinLength(8), MaxLength(128)] public string Senha { get; set; } = "";
    [Required] public PerfilUsuario Perfil { get; set; }
    public FuncaoFuncionario? Funcao { get; set; }
}
public class AtualizarUsuarioDto
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nome { get; set; } = "";
    [Required] public PerfilUsuario Perfil { get; set; }
    public FuncaoFuncionario? Funcao { get; set; }
}
public record UsuarioResposta(int Id, string Nome, string Email, PerfilUsuario Perfil, FuncaoFuncionario? Funcao, bool Ativo)
{
    public static UsuarioResposta De(Usuario u) => new(u.Id, u.Nome, u.Email, u.Perfil, u.Funcao, u.Ativo);
}
public class SetorDto
{
    [Required, StringLength(120, MinimumLength = 2)] public string Nome { get; set; } = "";
}
public class EquipamentoDto
{
    [Required, StringLength(150, MinimumLength = 2)] public string Nome { get; set; } = "";
    [Required, StringLength(60, MinimumLength = 1)] public string Patrimonio { get; set; } = "";
    [StringLength(100)] public string? Fabricante { get; set; }
    [StringLength(100)] public string? Modelo { get; set; }
    [StringLength(100)] public string? NumeroSerie { get; set; }
    public DateTime? DataAquisicao { get; set; }
    [Range(1, int.MaxValue)] public int SetorId { get; set; }
}
public class CriarOcorrenciaDto
{
    [Range(1, int.MaxValue)] public int EquipamentoId { get; set; }
    [Required, StringLength(2000, MinimumLength = 5)] public string Descricao { get; set; } = "";
    [Required] public PrioridadeOcorrencia Prioridade { get; set; }
}
public class CriarOrdemServicoDto
{
    [Required] public TipoManutencao TipoManutencao { get; set; }
    public int? OcorrenciaId { get; set; }
    public int? PlanoManutencaoId { get; set; }
}
public class RegistrarManutencaoDto
{
    [Required, StringLength(3000, MinimumLength = 5)] public string Diagnostico { get; set; } = "";
    [Required, StringLength(3000, MinimumLength = 5)] public string ServicoRealizado { get; set; } = "";
    [StringLength(3000)] public string? Observacoes { get; set; }
}
public class ConcluirOrdemServicoDto
{
    [Required, StringLength(3000, MinimumLength = 5)] public string Diagnostico { get; set; } = "";
    [Required, StringLength(3000, MinimumLength = 5)] public string ServicoRealizado { get; set; } = "";
    [StringLength(3000)] public string? Observacoes { get; set; }
    [Required] public bool? ProblemaResolvido { get; set; }
}
public class PlanoManutencaoDto
{
    [Range(1, int.MaxValue)] public int EquipamentoId { get; set; }
    [Required, StringLength(1000, MinimumLength = 5)] public string Descricao { get; set; } = "";
    [Range(1, 3650)] public int PeriodicidadeDias { get; set; }
    public DateTime? UltimaExecucao { get; set; }
    [Required] public DateTime ProximaExecucao { get; set; }
}
