using SistemaManutencao.API.Enums;

namespace SistemaManutencao.API.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public PerfilUsuario Perfil { get; set; }
    public FuncaoFuncionario? Funcao { get; set; }
    public bool Ativo { get; set; } = true;
}
public class Setor
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public bool Ativo { get; set; } = true;
}
public class Equipamento
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Patrimonio { get; set; } = "";
    public string? Fabricante { get; set; }
    public string? Modelo { get; set; }
    public string? NumeroSerie { get; set; }
    public DateTime? DataAquisicao { get; set; }
    public StatusEquipamento Status { get; set; } = StatusEquipamento.Funcionando;
    public int SetorId { get; set; }
    public Setor? Setor { get; set; }
    public bool Ativo { get; set; } = true;
}
public class Ocorrencia
{
    public int Id { get; set; }
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public string Descricao { get; set; } = "";
    public PrioridadeOcorrencia Prioridade { get; set; }
    public StatusOcorrencia Status { get; set; } = StatusOcorrencia.Aberta;
    public DateTime DataRegistro { get; set; } = DateTime.UtcNow;
    public OrdemServico? OrdemServico { get; set; }
}
public class OrdemServico
{
    public int Id { get; set; }
    public int? OcorrenciaId { get; set; }
    public Ocorrencia? Ocorrencia { get; set; }
    // Para OS preventiva manual sem ocorrência: extensão a alinhar com a pessoa do banco.
    public int? PlanoManutencaoId { get; set; }
    public PlanoManutencao? PlanoManutencao { get; set; }
    public int? TecnicoId { get; set; }
    public Usuario? Tecnico { get; set; }
    public TipoManutencao TipoManutencao { get; set; }
    public StatusOrdemServico Status { get; set; } = StatusOrdemServico.Aberta;
    public DateTime DataAbertura { get; set; } = DateTime.UtcNow;
    public Manutencao? Manutencao { get; set; }
}
public class Manutencao
{
    public int Id { get; set; }
    public int OrdemServicoId { get; set; }
    public OrdemServico? OrdemServico { get; set; }
    public string Diagnostico { get; set; } = "";
    public string ServicoRealizado { get; set; } = "";
    public string? Observacoes { get; set; }
    public DateTime DataInicio { get; set; }
    public DateTime? DataConclusao { get; set; }
    public bool? ProblemaResolvido { get; set; }
}
public class PlanoManutencao
{
    public int Id { get; set; }
    public int EquipamentoId { get; set; }
    public Equipamento? Equipamento { get; set; }
    public string Descricao { get; set; } = "";
    public int PeriodicidadeDias { get; set; }
    public DateTime? UltimaExecucao { get; set; }
    public DateTime ProximaExecucao { get; set; }
    public bool Ativo { get; set; } = true;
}
