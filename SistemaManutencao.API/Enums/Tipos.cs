namespace SistemaManutencao.API.Enums;

public enum PerfilUsuario { Administrador = 0, Funcionario = 1 }
public enum FuncaoFuncionario { Operador = 0, Tecnico = 1 }
public enum StatusEquipamento { Funcionando = 0, Parado = 1, EmManutencao = 2 }
public enum PrioridadeOcorrencia { Baixa = 0, Media = 1, Alta = 2 }
public enum StatusOcorrencia { Aberta = 0, EmAtendimento = 1, Resolvida = 2 }
public enum StatusOrdemServico { Aberta = 0, EmAtendimento = 1, Concluida = 2, Cancelada = 3 }
public enum TipoManutencao { Corretiva = 0, Preventiva = 1 }
