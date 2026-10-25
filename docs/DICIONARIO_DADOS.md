# Dicionário de Dados — PIM Sistema de Manutenção Industrial

**Banco:** SQL Server (LocalDB em desenvolvimento) · **ORM:** EF Core 8.0.11 · **DDL de referência:** `Migrations/20261020021711_InitialCreate.cs`
**Data:** 23/10/2026

Todos os tipos, nulabilidades e tamanhos abaixo foram extraídos da migration `InitialCreate` (tipos SQL Server reais), não inferidos das classes C#. Documentos complementares: `docs/DER_MODELO_FISICO.md` (diagrama) e `docs/GUIA_BANCO_INTEGRACAO.md` (migrations/integração).

**Convenções:** **PK** = chave primária com `IDENTITY (1,1)`; **FK** = chave estrangeira; **U** = índice único. Todas as datas usam `datetime2` e são armazenadas em **UTC** (a API exige sufixo `Z` nas entradas do usuário). Enums são persistidos como `int` (valores na seção 9).

---

## 1. Tabela `Usuarios`

Usuários do sistema (login JWT). `Perfil` distingue Administrador de Funcionário; `Funcao` só faz sentido para funcionários (NULL para administradores).

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador do usuário | Gerado pelo banco |
| `Nome` | `nvarchar(120)` | NÃO | — | 120 | Nome de exibição | Obrigatório |
| `Email` | `nvarchar(180)` | NÃO | **U** `IX_Usuarios_Email` (sem filtro) | 180 | Credencial de login | Único global; normalizado (trim/lower) pela API |
| `SenhaHash` | `nvarchar(500)` | NÃO | — | 500 | Hash da senha (PBKDF2, ASP.NET Identity `PasswordHasher`) | Nunca armazenar senha em texto |
| `Perfil` | `int` | NÃO | — | — | Papel de acesso | Enum `PerfilUsuario` (0 Administrador, 1 Funcionario) |
| `Funcao` | `int` | **SIM** | — | — | Função quando funcionário | Enum `FuncaoFuncionario` (0 Operador, 1 Tecnico); NULL para Administrador |
| `Ativo` | `bit` | NÃO | — | — | Soft delete lógico | Default `true`; inativo não autentica |

PK: `PK_Usuarios (Id)`.

## 2. Tabela `Setores`

Setores/áreas da planta industrial.

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador do setor | Gerado pelo banco |
| `Nome` | `nvarchar(120)` | NÃO | **U** `IX_Setores_Nome` — filtro `[Ativo] = 1` | 120 | Nome do setor | Dois setores **ativos** não podem repetir nome; nome pode ser reutilizado por setor inativo |
| `Ativo` | `bit` | NÃO | — | — | Soft delete lógico | Default `true` |

PK: `PK_Setores (Id)`.

## 3. Tabela `Equipamentos`

Máquinas e equipamentos monitorados. Referencia o setor onde estão instalados.

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador do equipamento | Gerado pelo banco |
| `Nome` | `nvarchar(150)` | NÃO | — | 150 | Nome do equipamento | Obrigatório |
| `Patrimonio` | `nvarchar(60)` | NÃO | **U** `IX_Equipamentos_Patrimonio` (sem filtro) | 60 | Número de patrimônio físico | Único global |
| `Fabricante` | `nvarchar(100)` | **SIM** | — | 100 | Fabricante | Opcional |
| `Modelo` | `nvarchar(100)` | **SIM** | — | 100 | Modelo | Opcional |
| `NumeroSerie` | `nvarchar(100)` | **SIM** | — | 100 | Número de série | Opcional |
| `DataAquisicao` | `datetime2` | **SIM** | — | — | Data de aquisição | Opcional; UTC |
| `Status` | `int` | NÃO | — | — | Estado operacional | Enum `StatusEquipamento` (0 Funcionando, 1 Parado, 2 EmManutencao); default `Funcionando` |
| `SetorId` | `int` | NÃO | **FK1** → `Setores.Id` (`FK_Equipamentos_Setores_SetorId`, Restrict) | — | Setor do equipamento | Obrigatório; setor não é excluído enquanto referenciado |
| `Ativo` | `bit` | NÃO | — | — | Soft delete lógico | Default `true` |

PK: `PK_Equipamentos (Id)`. Índice convencional: `IX_Equipamentos_SetorId`.

## 4. Tabela `Ocorrencias`

Registro de falha/problema reportado para um equipamento.

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador da ocorrência | Gerado pelo banco |
| `EquipamentoId` | `int` | NÃO | **FK2** → `Equipamentos.Id` (`FK_Ocorrencias_Equipamentos_EquipamentoId`, Restrict) | — | Equipamento com o problema | Único entre ocorrências ativas: `IX_Ocorrencias_EquipamentoId` **U** filtro `[Status] <> 2` |
| `UsuarioId` | `int` | NÃO | **FK3** → `Usuarios.Id` (`FK_Ocorrencias_Usuarios_UsuarioId`, Restrict) | — | Usuário que registrou | Operador ou Administrador (permissão extra do Admin em ratificação — pendência D11) |
| `Descricao` | `nvarchar(2000)` | NÃO | — | 2000 | Descrição do problema | Obrigatória |
| `Prioridade` | `int` | NÃO | — | — | Prioridade do atendimento | Enum `PrioridadeOcorrencia` (0 Baixa, 1 Media, 2 Alta) |
| `Status` | `int` | NÃO | — | — | Estado do atendimento | Enum `StatusOcorrencia` (0 Aberta, 1 EmAtendimento, 2 Resolvida); default `Aberta`. `Resolvida = 2` é o valor excluído pelo índice único filtrado |
| `DataRegistro` | `datetime2` | NÃO | — | — | Momento do registro | Default UTC do servidor (`DateTime.UtcNow`) |

PK: `PK_Ocorrencias (Id)`. Índice convencional: `IX_Ocorrencias_UsuarioId`.

## 5. Tabela `PlanosManutencao`

Planos de manutenção **preventiva** por equipamento, com periodicidade.

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador do plano | Gerado pelo banco |
| `EquipamentoId` | `int` | NÃO | **FK8** → `Equipamentos.Id` (`FK_PlanosManutencao_Equipamentos_EquipamentoId`, Restrict) | — | Equipamento alvo do plano | Obrigatório; plano com OS vinculada não pode trocar de equipamento (regra de serviço) |
| `Descricao` | `nvarchar(1000)` | NÃO | — | 1000 | Descrição das atividades do plano | Obrigatória |
| `PeriodicidadeDias` | `int` | NÃO | — | — | Intervalo entre execuções (dias) | Inteiro positivo |
| `UltimaExecucao` | `datetime2` | **SIM** | — | — | Última execução realizada | NULL até a primeira OS preventiva concluir |
| `ProximaExecucao` | `datetime2` | NÃO | — | — | Data-alvo da próxima execução | Obrigatória; UTC |
| `Ativo` | `bit` | NÃO | — | — | Soft delete lógico | Default `true` |

PK: `PK_PlanosManutencao (Id)`. Índice convencional: `IX_PlanosManutencao_EquipamentoId`.

## 6. Tabela `OrdensServicos`

Ordens de serviço de manutenção — corretivas (originadas de ocorrência) ou preventivas (originadas de plano).

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador da OS | Gerado pelo banco |
| `OcorrenciaId` | `int` | **SIM** | **FK4** → `Ocorrencias.Id` (`FK_OrdensServicos_Ocorrencias_OcorrenciaId`, Restrict) | — | Ocorrência de origem (corretiva) | **Nulável por extensão aprovada** (ver seção 10). **U** `IX_OrdensServicos_OcorrenciaId` filtro `[OcorrenciaId] IS NOT NULL` — no máximo 1 OS por ocorrência (1:1) |
| `PlanoManutencaoId` | `int` | **SIM** | **FK5** → `PlanosManutencao.Id` (`FK_OrdensServicos_PlanosManutencao_PlanoManutencaoId`, Restrict) | — | Plano de origem (preventiva) | **Coluna extensão aprovada** (ver seção 10); sem índice único — um plano pode gerar várias OS ao longo do tempo |
| `TecnicoId` | `int` | **SIM** | **FK6** → `Usuarios.Id` (`FK_OrdensServicos_Usuarios_TecnicoId`, Restrict) | — | Técnico responsável | NULL até atribuição/assunção; obrigatório para iniciar/atender/concluir |
| `TipoManutencao` | `int` | NÃO | — | — | Natureza da OS | Enum `TipoManutencao` (0 Corretiva, 1 Preventiva). **Governado pela CHECK `CK_OrdensServicos_TipoOrigem`** (seção 8) |
| `Status` | `int` | NÃO | — | — | Estado da OS | Enum `StatusOrdemServico` (0 Aberta, 1 EmAtendimento, 2 Concluida, 3 Cancelada); default `Aberta` |
| `DataAbertura` | `datetime2` | NÃO | — | — | Momento da abertura | Default UTC do servidor (`DateTime.UtcNow`) |

PK: `PK_OrdensServicos (Id)`. Índices convencionais: `IX_OrdensServicos_PlanoManutencaoId`, `IX_OrdensServicos_TecnicoId`.

## 7. Tabela `Manutencoes`

Execução da manutenção de uma OS: diagnóstico, serviço realizado, datas e resultado.

| Coluna | Tipo SQL Server | Nulo | Chave / Identidade | Máx. | Finalidade | Regras / Defaults |
|---|---|---|---|---|---|---|
| `Id` | `int` | NÃO | PK, IDENTITY (1,1) | — | Identificador da manutenção | Gerado pelo banco |
| `OrdemServicoId` | `int` | NÃO | **FK7** → `OrdensServicos.Id` (`FK_Manutencoes_OrdensServicos_OrdemServicoId`, Restrict) | — | OS executada | **U** `IX_Manutencoes_OrdemServicoId` (sem filtro) — no máximo 1 manutenção por OS (1:1) |
| `Diagnostico` | `nvarchar(3000)` | NÃO | — | 3000 | Diagnóstico técnico | Obrigatório (na conclusão, ou registrado antes em `PUT /manutencao`) |
| `ServicoRealizado` | `nvarchar(3000)` | NÃO | — | 3000 | Descrição do serviço executado | Obrigatório (mesma flexibilidade do diagnóstico) |
| `Observacoes` | `nvarchar(3000)` | **SIM** | — | 3000 | Observações adicionais | Opcional; observações anteriores são preservadas se omitidas na conclusão |
| `DataInicio` | `datetime2` | NÃO | — | — | Início do atendimento | UTC; aceita `dataInicio` informado (D5) ou usa `DateTime.UtcNow` |
| `DataConclusao` | `datetime2` | **SIM** | — | — | Conclusão do atendimento | **NULL enquanto em andamento**; nunca anterior a `DataInicio` (validado pela API) |
| `ProblemaResolvido` | `bit` | **SIM** | — | — | Resultado informado na conclusão | **Coluna extensão aprovada** (ver seção 10): NULL = não concluída; `true` = SIM; `false` = NÃO |

PK: `PK_Manutencoes (Id)`.

---

## 8. CHECK constraint `CK_OrdensServicos_TipoOrigem` ⭐ Implementação aprovada 2026-10-23

**Definição SQL Server** (criada pela migration incremental `20261023061811_OrdemServicoTipoOrigemCheck`, ainda **não aplicada** no banco oficial `SistemaManutencaoPIM`; NÃO integra a `InitialCreate`):

```sql
ALTER TABLE [OrdensServicos] ADD CONSTRAINT [CK_OrdensServicos_TipoOrigem] CHECK (
    ([TipoManutencao] = 0 AND [OcorrenciaId] IS NOT NULL AND [PlanoManutencaoId] IS NULL)
    OR
    ([TipoManutencao] = 1 AND [PlanoManutencaoId] IS NOT NULL AND [OcorrenciaId] IS NULL)
);
```

**Semântica — exatamente uma origem por OS, coerente com o tipo:**

| Cenário | `TipoManutencao` | `OcorrenciaId` | `PlanoManutencaoId` | Resultado da CHECK |
|---|---|---|---|---|
| OS corretiva (de ocorrência) | 0 Corretiva | NOT NULL | NULL | ✅ Aceita |
| OS preventiva (de plano) | 1 Preventiva | NULL | NOT NULL | ✅ Aceita |
| OS sem origem nenhuma | qualquer | NULL | NULL | ❌ Rejeita |
| OS com as duas origens | qualquer | NOT NULL | NOT NULL | ❌ Rejeita |
| Corretiva apontando para plano | 0 Corretiva | NULL | NOT NULL | ❌ Rejeita |
| Preventiva apontando para ocorrência | 1 Preventiva | NOT NULL | NULL | ❌ Rejeita |

A CHECK **não substitui** as validações da API (serviços) nem o índice único filtrado de `OcorrenciaId` — ela é a última barreira de integridade no banco, válida inclusive para escritas fora da API. Rejeição pelo SQL Server gera erro de CHECK (classe 16), que a API surface-iza como conflito (HTTP 409).

---

## 9. Enums persistidos (`int`) — valores numéricos

Fonte: `SistemaManutencao.API/Enums/Tipos.cs`. No banco as colunas são `int`; a API expõe os valores como **texto** (JSON string) nos contratos.

| Enum | Valores |
|---|---|
| `PerfilUsuario` | Administrador = 0 · Funcionario = 1 |
| `FuncaoFuncionario` | Operador = 0 · Tecnico = 1 |
| `StatusEquipamento` | Funcionando = 0 · Parado = 1 · EmManutencao = 2 |
| `PrioridadeOcorrencia` | Baixa = 0 · Media = 1 · Alta = 2 |
| `StatusOcorrencia` | Aberta = 0 · EmAtendimento = 1 · Resolvida = 2 |
| `StatusOrdemServico` | Aberta = 0 · EmAtendimento = 1 · Concluida = 2 · Cancelada = 3 |
| `TipoManutencao` | Corretiva = 0 · Preventiva = 1 |

Valores críticos referenciados por filtros de índice: `StatusOcorrencia.Resolvida = 2` (filtro de `IX_Ocorrencias_EquipamentoId`) e `Setores.Ativo = 1` (filtro de `IX_Setores_Nome`).

## 10. Extensões em relação ao `ResumoPIM.docx`

Três diferenças deliberadas entre o modelo do documento do grupo e o banco oficial (preservadas por decisão de 23/10/2026; alinhamento com o banco do grupo segue listado em `docs/PENDENCIAS_E_DECISOES.md` como pendência):

1. **`OrdensServicos.OcorrenciaId` NULLABLE** — no DOCX, toda OS nasceria de uma ocorrência (coluna NOT NULL). No banco oficial ela é nulável porque **a OS preventiva não tem ocorrência** e a **OS preventiva manual** é criada a partir de um plano (`POST` preventiva sem ocorrência corretiva). O caráter 1:1 com ocorrências é preservado pelo índice único filtrado `[OcorrenciaId] IS NOT NULL`, não pela nulabilidade.
2. **`OrdensServicos.PlanoManutencaoId`** — coluna **não existente no DOCX**. Adicionada para que a preventiva tenha origem formal (plano) e a corretiva tenha origem formal (ocorrência), com as duas mutuamente exclusivas — exclusividade agora também garantida no banco pela CHECK `CK_OrdensServicos_TipoOrigem` (seção 8).
3. **`Manutencoes.ProblemaResolvido`** — coluna **não existente no DOCX**. O fluxo de conclusão exige que o técnico informe **SIM/NÃO** para "o problema foi resolvido?" (dirige os estados finais da ocorrência e do equipamento). `bit NULL`: NULL = manutenção em andamento, `true` = SIM, `false` = NÃO.

### Por que `TempoExecucao` NÃO é persistido

O DOCX previa um campo de tempo de execução. No banco oficial ele **não existe como coluna** — é **dado calculado**, não persistido:

```
TempoExecucao = Manutencoes.DataConclusao − Manutencoes.DataInicio
```

Motivos: (a) evita redundância/desvio entre valor armazenado e datas reais (uma única fonte de verdade); (b) enquanto a manutenção está em andamento `DataConclusao` é **NULL**, e um `TempoExecucao` persistido ficaria indefinido ou exigiria atualização a cada tick; (c) qualquer agregação (média por técnico/equipamento) é feita em consulta, no momento da leitura, sempre consistente com as datas. A API nunca recebe `TempoExecucao` do cliente — recebe apenas `dataInicio`/`dataConclusao` em UTC.

---

## 11. Lista completa de índices (11)

| Índice | Tabela | Coluna(s) | Único? | Filtro |
|---|---|---|---|---|
| `IX_Usuarios_Email` | Usuarios | Email | **Sim** | — |
| `IX_Setores_Nome` | Setores | Nome | **Sim** | `[Ativo] = 1` |
| `IX_Equipamentos_Patrimonio` | Equipamentos | Patrimonio | **Sim** | — |
| `IX_Equipamentos_SetorId` | Equipamentos | SetorId | Não | — |
| `IX_Ocorrencias_EquipamentoId` | Ocorrencias | EquipamentoId | **Sim** | `[Status] <> 2` |
| `IX_Ocorrencias_UsuarioId` | Ocorrencias | UsuarioId | Não | — |
| `IX_PlanosManutencao_EquipamentoId` | PlanosManutencao | EquipamentoId | Não | — |
| `IX_OrdensServicos_OcorrenciaId` | OrdensServicos | OcorrenciaId | **Sim** | `[OcorrenciaId] IS NOT NULL` |
| `IX_OrdensServicos_PlanoManutencaoId` | OrdensServicos | PlanoManutencaoId | Não | — |
| `IX_OrdensServicos_TecnicoId` | OrdensServicos | TecnicoId | Não | — |
| `IX_Manutencoes_OrdemServicoId` | Manutencoes | OrdemServicoId | **Sim** | — |

Os 5 índices não únicos (`IX_Equipamentos_SetorId`, `IX_Ocorrencias_UsuarioId`, `IX_PlanosManutencao_EquipamentoId`, `IX_OrdensServicos_PlanoManutencaoId`, `IX_OrdensServicos_TecnicoId`) são convencionais, criados pelo EF Core para cada FK.

## 12. Lista completa das 8 chaves estrangeiras

| # | Constraint | Coluna (tabela filha) | Referencia | Nula? | `DeleteBehavior` |
|---|---|---|---|---|---|
| 1 | `FK_Equipamentos_Setores_SetorId` | `SetorId` (Equipamentos) | `Setores.Id` | Não | Restrict |
| 2 | `FK_Ocorrencias_Equipamentos_EquipamentoId` | `EquipamentoId` (Ocorrencias) | `Equipamentos.Id` | Não | Restrict |
| 3 | `FK_Ocorrencias_Usuarios_UsuarioId` | `UsuarioId` (Ocorrencias) | `Usuarios.Id` | Não | Restrict |
| 4 | `FK_OrdensServicos_Ocorrencias_OcorrenciaId` | `OcorrenciaId` (OrdensServicos) | `Ocorrencias.Id` | Sim | Restrict |
| 5 | `FK_OrdensServicos_PlanosManutencao_PlanoManutencaoId` | `PlanoManutencaoId` (OrdensServicos) | `PlanosManutencao.Id` | Sim | Restrict |
| 6 | `FK_OrdensServicos_Usuarios_TecnicoId` | `TecnicoId` (OrdensServicos) | `Usuarios.Id` | Sim | Restrict |
| 7 | `FK_Manutencoes_OrdensServicos_OrdemServicoId` | `OrdemServicoId` (Manutencoes) | `OrdensServicos.Id` | Não | Restrict |
| 8 | `FK_PlanosManutencao_Equipamentos_EquipamentoId` | `EquipamentoId` (PlanosManutencao) | `Equipamentos.Id` | Não | Restrict |

`Restrict` = nenhuma exclusão de pai com filhos existentes (o `Down()` da migration exclui na ordem inversa de dependências). Histórico é desativado por `Ativo = 0`, nunca apagado.
