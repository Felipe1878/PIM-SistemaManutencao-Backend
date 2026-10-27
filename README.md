# Sistema de Manutenção Industrial — Backend e Banco de Dados

**API REST | ASP.NET Core 8 + Entity Framework Core 8.0.11 + SQL Server**

Sistema de gestão de manutenção industrial (CMMS) desenvolvido como Projeto Integrado Multidisciplinar (PIM). O backend centraliza autenticação, regras de negócio e o acesso aos dados de equipamentos, ocorrências, ordens de serviço, manutenções corretivas e planos preventivos.

As aplicações web (React) e mobile (Flutter), desenvolvidas pelos demais integrantes da equipe, consomem os endpoints HTTP desta API. Nenhum cliente acessa o SQL Server diretamente.

Este repositório entrega o **Backend e o Banco de Dados do PIM**: código-fonte, migrations, modelo físico, testes e documentação de integração.

O modelo físico implementado neste repositório define o banco oficial local (`SistemaManutencaoPIM`). Alterações de esquema passam a ser feitas apenas por migrations deste repositório, nunca por edição direta no banco.

## Principais funcionalidades

- **Autenticação e autorização:** login JWT e controle de acesso por perfil.
- **Gestão administrativa:** cadastro e consulta de usuários, setores e equipamentos.
- **Ocorrências:** registro, acompanhamento e filtros por equipamento, descrição e status.
- **Ordens de serviço:** criação, atribuição, início, atendimento, conclusão e cancelamento.
- **Manutenção industrial:** registro de diagnóstico, serviço realizado, observações e datas.
- **Manutenção preventiva:** gerenciamento de planos e criação manual de ordens de serviço.
- **Dashboard:** consulta de indicadores administrativos.
- **Integridade:** validações de negócio, controle de estados e proteção contra atribuição simultânea da mesma OS.

A referência completa de endpoints está disponível em [Contratos da API](docs/CONTRATOS_API.md).

## Arquitetura e tecnologias

| Tecnologia | Utilização |
|---|---|
| C# / ASP.NET Core 8 | Desenvolvimento da API REST |
| Entity Framework Core 8.0.11 | Persistência, relacionamentos e migrations |
| SQL Server | Banco de dados relacional (LocalDB em desenvolvimento) |
| JWT Bearer | Autenticação e autorização |
| Swagger / OpenAPI | Documentação dos endpoints |
| Postman | Demonstração dos fluxos HTTP |
| xUnit / WebApplicationFactory / SQLite | Testes automatizados de API |
| xUnit / SQL Server real | Testes de integração da CHECK de tipo de origem e fluxos E2E ponta a ponta |

A API mantém as regras de negócio nos serviços, enquanto os controllers recebem as requisições HTTP e os DTOs definem os contratos de entrada e saída.

```mermaid
flowchart LR
    R["React (Web)"] -->|HTTP/JSON| A["API ASP.NET Core"]
    F["Flutter (Mobile)"] -->|HTTP/JSON| A
    A -->|EF Core| S[("SQL Server")]
```

Os clientes não acessam o banco diretamente. Toda comunicação com os dados passa pela API.

## Modelo de dados

O esquema físico definido neste repositório:

- **7 tabelas:** `Usuarios`, `Setores`, `Equipamentos`, `Ocorrencias`, `OrdensServicos`, `Manutencoes` e `PlanosManutencao`.
- **8 chaves estrangeiras**, todas com exclusão `Restrict` (nenhuma exclusão em cascata).
- **6 índices únicos**, sendo 3 filtrados (parciais).
- **CHECK `CK_OrdensServicos_TipoOrigem`:** ordem corretiva exige `OcorrenciaId` e não pode ter `PlanoManutencaoId`; ordem preventiva exige `PlanoManutencaoId` e não pode ter `OcorrenciaId`.

Em relação ao modelo do DOCX da disciplina, o backend implementa as extensões abaixo, registradas no dicionário de dados; não há evidência de aprovação formal do esquema completo pelo grupo:

- `OrdemServico.OcorrenciaId` nullable, para permitir ordens preventivas sem ocorrência.
- `OrdemServico.PlanoManutencaoId`, que registra a origem preventiva da ordem.
- `Manutencao.ProblemaResolvido`, resultado informado pelo técnico na conclusão.
- `TempoExecucao` não é persistido: é calculado como `DataConclusao - DataInicio`.

Documentação do banco:

- [DER e modelo físico](docs/DER_MODELO_FISICO.md): diagrama, tabelas, chaves e índices.
- [Dicionário de dados](docs/DICIONARIO_DADOS.md): colunas, tipos, nulabilidade e domínios.
- [Guia de banco de dados](docs/GUIA_BANCO_INTEGRACAO.md): configuração, migrations, backup e handoff.

## Regras de negócio

### Fluxo de uma manutenção corretiva

1. **Operador:** registra uma ocorrência para um equipamento.
2. **Administrador:** cria uma ordem de serviço vinculada à ocorrência.
3. **Técnico:** assume a OS, inicia o atendimento e registra a manutenção.
4. **Técnico:** solicita a conclusão, informando se o problema foi resolvido.
5. **API:** conclui a OS e atualiza os estados da ocorrência e do equipamento conforme o resultado.

| Evento | Comportamento da API |
|---|---|
| Ocorrência registrada | Equipamento passa para `Parado` |
| Manutenção iniciada | OS passa para `EmAtendimento` e equipamento para `EmManutencao` |
| Problema resolvido | OS `Concluida`, ocorrência `Resolvida` e equipamento `Funcionando` |
| Problema não resolvido | OS `Concluida`, ocorrência `Aberta` e equipamento `Parado` |

Outras regras implementadas:

- Um equipamento pode ter apenas uma ocorrência ativa por vez.
- Cada ocorrência corretiva pode estar vinculada a uma única OS.
- Uma OS preventiva pode ser criada a partir de um plano, sem ocorrência corretiva.
- Somente o técnico responsável pode iniciar, registrar e concluir uma manutenção.
- Dois técnicos não conseguem assumir simultaneamente a mesma OS.
- Um plano com OS vinculada não pode ser transferido para outro equipamento.
- Datas de início e conclusão aceitam valores UTC com sufixo `Z`. Quando omitidas, a API utiliza o horário UTC do servidor.

### Perfis e permissões

| Funcionalidade | Administrador | Operador | Técnico |
|---|:---:|:---:|:---:|
| Consultar equipamentos e setores | Sim | Sim | Sim |
| Registrar e consultar ocorrências | Sim | Sim | Não |
| Consultar ordens de serviço | Sim | Não | Sim |
| Assumir e executar manutenção | Não | Não | Sim |
| Criar e cancelar ordens de serviço | Sim | Não | Não |
| Gerenciar usuários, setores, equipamentos e planos | Sim | Não | Não |
| Consultar dashboard | Sim | Não | Não |

A API atualmente permite que Administradores e Operadores registrem ocorrências. Essa permissão adicional do Administrador ainda depende de ratificação do grupo (D11).

As decisões sobre a continuidade de manutenções não resolvidas (D1 e D2) também permanecem documentadas em [Pendências e decisões](docs/PENDENCIAS_E_DECISOES.md).

## Instalação e execução

### Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- SQL Server LocalDB, Express ou Developer.
- Acesso à internet para restaurar os pacotes NuGet.

Para gerenciar migrations manualmente, instale a ferramenta do Entity Framework Core:

```powershell
dotnet tool install --global dotnet-ef --version 8.0.11
```

### 1. Restaurar e compilar

No terminal, abra a pasta principal do repositório, onde está o arquivo `SistemaManutencaoPIM.sln`:

```powershell
dotnet restore
dotnet build
```

### 2. Aplicar as migrations (caminho controlado)

Duas migrations compõem o esquema atual:

| Migration | Conteúdo | Situação no banco oficial |
|---|---|---|
| `20261020021711_InitialCreate` | Criação das 7 tabelas, 8 FKs, índices e constraints | Aplicada |
| `20261023061811_OrdemServicoTipoOrigemCheck` | CHECK `CK_OrdensServicos_TipoOrigem` | Aplicada e validada no banco oficial local (25/10/2026) |

Situação por ambiente:

1. **Banco oficial local `SistemaManutencaoPIM`:** as duas migrations estão aplicadas e validadas em 25/10/2026 — backup prévio verificado (`SistemaManutencaoPIM-20261025-020253.bak`, `RESTORE VERIFYONLY` OK), `__EFMigrationsHistory` com as duas entradas, CHECK ativa e confiável (`is_disabled = 0`, `is_not_trusted = 0`), contagens de dados preservadas e hash SHA-256 do Administrador ID 1 inalterado.
2. **Esquema reproduzível:** em qualquer máquina nova, o comando abaixo aplica as duas migrations a partir do repositório.
3. **Banco definitivo compartilhado do grupo:** ainda não homologado — a aplicação lá continua pendente, após auditoria prévia e alinhamento com o responsável pelo SQL Server, conforme o procedimento de [Guia de banco de dados, seção 8.3](docs/GUIA_BANCO_INTEGRACAO.md).

```powershell
dotnet ef database update --project SistemaManutencao.API
```

### 3. Iniciar a API

No Windows, utilizando PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\iniciar-dev.ps1
```

O script configura o ambiente de desenvolvimento e utiliza `admin@pim.local` como e-mail padrão do administrador inicial.

Se `SeedAdmin__Senha` não estiver configurada, solicita uma senha. O seed é idempotente (`Data/DbInitializer.cs`): o administrador só é cadastrado quando a tabela `Usuarios` está vazia.

Se `Jwt__Key` não estiver definida, o script gera uma chave aleatória para a execução. Tokens gerados anteriormente deixam de funcionar quando a API é reiniciada com uma chave diferente.

Atenção ao flag automático de migrations em Development. Com `Database:ApplyMigrationsOnStartup = true` (configuração atual de `appsettings.Development.json`), qualquer boot da API aplica todas as migrations pendentes no banco apontado pela connection string (`Program.cs:117-127`). No banco oficial local as duas migrations já estão aplicadas e validadas; o cuidado permanece necessário para não migrar automaticamente outra base sem auditoria, em especial o banco definitivo compartilhado do grupo, ainda não homologado. Para iniciar sem aplicar migrations:

```powershell
$env:Database__ApplyMigrationsOnStartup = "false"
```

Esse fluxo está detalhado em [Guia de banco de dados, seção 2.2](docs/GUIA_BANCO_INTEGRACAO.md).

Por padrão, o Swagger fica disponível em:

**http://localhost:5088/swagger**

Se não utilizar LocalDB, configure `ConnectionStrings__DefaultConnection` para um banco de desenvolvimento.

Não utilize o banco compartilhado do grupo sem alinhar previamente a estrutura com o responsável pelo SQL Server.

## Autenticação JWT

O login é feito pelo endpoint:

`POST /api/auth/login`

Exemplo de requisição:

```json
{
  "email": "admin@pim.local",
  "senha": "SENHA_DEFINIDA_NA_INICIALIZACAO"
}
```

A resposta contém o token JWT, o tipo `Bearer`, a expiração em UTC e os dados do usuário autenticado.

Nas rotas protegidas, utilize:

```http
Authorization: Bearer ***
```

Para consultar os dados do usuário logado:

```http
GET /api/auth/me
Authorization: Bearer ***
```

As permissões são verificadas pela API. O acesso pode ser negado quando o token está ausente, expirado, inválido ou quando o usuário não possui a autorização necessária.

Não publique tokens, senhas ou chaves em arquivos versionados.

## Testando pelo Swagger

Com a API e o banco de desenvolvimento em execução, é possível demonstrar o fluxo corretivo:

1. Faça login como Administrador em `/api/auth/login` e configure o token no botão **Authorize**.
2. Cadastre um setor, um equipamento, um Operador e um Técnico.
3. Entre como Operador e registre uma ocorrência em `POST /api/ocorrencias`.
4. Entre como Administrador e crie uma OS corretiva em `POST /api/ordens-servico`.
5. Entre como Técnico e utilize as ações `assumir` e `iniciar` da OS.
6. Registre diagnóstico e serviço realizado em `PUT /api/ordens-servico/{id}/manutencao`, ou informe esses textos diretamente na conclusão.
7. Conclua a OS em `POST /api/ordens-servico/{id}/concluir`, informando `problemaResolvido`.
8. Consulte novamente a OS, a ocorrência e o equipamento para conferir os estados atualizados.

Os campos `prioridade`, `tipoManutencao` e `perfil` são obrigatórios nos respectivos contratos.

Utilize os IDs reais retornados pela API. Os exemplos da documentação são apenas ilustrativos.

Uma coleção Postman com **21 requisições organizadas em sequência** também está disponível:

[Coleção Postman](docs/SistemaManutencaoPIM.postman_collection.json)

## Contratos e respostas de erro

Os clientes React e Flutter devem seguir os contratos publicados em [docs/CONTRATOS_API.md](docs/CONTRATOS_API.md).

Pontos importantes para integração:

- As respostas utilizam JSON com propriedades em `camelCase`.
- Os enums são representados como texto.
- Datas de manutenção devem ser enviadas em UTC, com sufixo `Z`.
- `dataInicio` e `dataConclusao` são opcionais.
- A data de conclusão não pode ser anterior ao início da manutenção.
- Diagnóstico e serviço realizado podem ser registrados durante o atendimento ou informados na conclusão.
- Observações anteriormente registradas são preservadas quando omitidas na conclusão.

### Status HTTP

| Status | Significado |
|---|---|
| `400` | Dados inválidos ou requisitos da operação não atendidos |
| `401` | Falha de autenticação |
| `403` | Usuário sem permissão |
| `404` | Recurso não encontrado |
| `409` | Conflito com o estado ou regras de negócio |
| `500` | Erro interno inesperado |

Erros de negócio e de autenticação/autorização utilizam o formato:

```json
{
  "erro": "Mensagem descritiva"
}
```

Erros automáticos de validação de modelo utilizam `ValidationProblemDetails`, com os detalhes no campo `errors`.

Os clientes devem tratar os dois formatos.

### CORS

No ambiente Development, estão configuradas as origens:

- `http://localhost:3000`
- `http://localhost:5173`
- `http://localhost:8080`

Em outros ambientes, configure `Cors:Origens` conforme os endereços utilizados pelo frontend. Sem origens configuradas, nenhuma origem de navegador é liberada por essa política.

## Testes e validação

A suíte tem **126 casos de teste, todos aprovados na última execução (126/126, 0 falhas, 0 ignorados)**, em duas camadas:

| Camada | Casos | Recursos |
|---|---|---|
| API, regras de negócio e segurança da fixture (2 regressões) | 108 | xUnit, `WebApplicationFactory` e SQLite em memória |
| Integração com SQL Server real | 18 | Classe `SqlServerTipoOrigemCheckTests` (8 casos da CHECK) + 10 casos E2E ponta a ponta em `E2EApiSqlServerTests.cs` (3 classes: corretiva, preventiva, integridade/autorização), trait `Categoria=SqlServer` |

Os 18 testes de SQL Server rodam contra bancos descartáveis, nunca contra o oficial: `SistemaManutencaoPIM_ValidationTemp` (8 casos que exercitam a CHECK `CK_OrdensServicos_TipoOrigem`) e `SistemaManutencaoPIM_E2E_<GUID>` (10 casos E2E — 1 de fluxo corretivo, 2 de fluxo preventivo e 7 de integridade/autorização — que percorrem a API real, com migrations e seed aplicados pelo startup da própria API). Ambos são criados e removidos pelos próprios testes.

### Como rodar

Suíte completa, que exige LocalDB disponível na máquina:

```powershell
dotnet test
```

Somente a camada SQLite/API, para máquinas sem SQL Server LocalDB:

```powershell
dotnet test --filter "Categoria!=SqlServer"
```

Atenção: sem LocalDB, esses 18 testes de SQL Server falham. Eles não são ignorados e nunca são marcados como aprovados — a falha é real e significa apenas que a validação contra o SQL Server não pôde rodar naquela máquina. Nesse cenário, utilize o filtro acima para executar os 108 casos de API.

## Status de entrega

### 1. Concluído e validado

- API completa: autenticação JWT, regras de negócio, contratos HTTP, Swagger e coleção Postman.
- Modelo físico implementado e validado no banco oficial local (sem afirmar aprovação formal do grupo): 7 tabelas, 8 FKs `Restrict`, 6 índices únicos e a CHECK de tipo de origem.
- Migrations `20261020021711_InitialCreate` e `20261023061811_OrdemServicoTipoOrigemCheck` aplicadas e validadas no banco oficial local `SistemaManutencaoPIM` em 25/10/2026: backup prévio verificado (`SistemaManutencaoPIM-20261025-020253.bak`, `RESTORE VERIFYONLY` OK), `__EFMigrationsHistory` com as duas migrations, CHECK com `is_disabled = 0` e `is_not_trusted = 0`, contagens preservadas e hash SHA-256 do Administrador ID 1 inalterado.
- API validada em execução real contra o banco oficial local: health 200, login do Administrador 200, 11 GETs autenticados 200, controles sem token 401 e Swagger 200 — API iniciada sem migrations automáticas (`Database__ApplyMigrationsOnStartup=false`) e encerrada após os testes, com a porta 5088 liberada.
- 126/126 testes aprovados (108 SQLite/API e 18 contra SQL Server real: 8 da CHECK + 10 E2E ponta a ponta).
- Documentação de banco publicada: DER, dicionário de dados e guia de banco de dados.

### 2. Pendente de autorização

- No banco oficial local as duas migrations já estão aplicadas e validadas (25/10/2026); a pendência restante é aplicar e validar as migrations no **banco compartilhado do grupo**, mediante auditoria prévia e decisão do grupo, conforme o procedimento de [Guia de banco de dados, seção 8.3](docs/GUIA_BANCO_INTEGRACAO.md).
- Em máquina nova não há pendência: o repositório contém as migrations e `dotnet ef database update --project SistemaManutencao.API` reproduz o esquema completo.
- Até a homologação do banco compartilhado, continuar iniciando a API em Development com `Database:ApplyMigrationsOnStartup = "false"` (comando na seção de execução), para não migrar automaticamente nenhuma base sem auditoria.

### 3. Integrações futuras

- Aplicar e validar as migrations no banco compartilhado do grupo (SQL Server definitivo), conforme [Guia de banco de dados, seção 8.3](docs/GUIA_BANCO_INTEGRACAO.md).
- Decidir e implementar as pendências D1 (continuidade da corretiva), D2 (preventiva não resolvida) e D11 (ocorrência pelo Administrador), documentadas em [Pendências e decisões](docs/PENDENCIAS_E_DECISOES.md).
- Consumo efetivo pelos frontends React e Flutter, seguindo os contratos de [docs/CONTRATOS_API.md](docs/CONTRATOS_API.md) e [docs/openapi.json](docs/openapi.json).
- Publicação e deploy do backend.

## Entrega ao líder

Resumo do que está entregue neste repositório (Backend e Banco de Dados do PIM).

### Escopo atendido

- Backend/API completa e banco de dados: 7 tabelas, 8 chaves estrangeiras `Restrict`, 6 índices únicos e a CHECK `CK_OrdensServicos_TipoOrigem` de origem da ordem de serviço.
- Migrations `20261020021711_InitialCreate` e `20261023061811_OrdemServicoTipoOrigemCheck` aplicadas e validadas no banco oficial local `SistemaManutencaoPIM` (25/10/2026).

### Tecnologias

- C# / ASP.NET Core 8 (API REST).
- Entity Framework Core 8.0.11 (persistência, relacionamentos e migrations).
- SQL Server, com LocalDB em desenvolvimento.
- JWT Bearer (autenticação e autorização por perfil).
- Swagger / OpenAPI (documentação dos endpoints).
- xUnit (testes automatizados).

### Funcionalidades implementadas

- Autenticação JWT e controle de acesso por perfil (Administrador, Operador, Técnico).
- Gestão administrativa de usuários, setores e equipamentos.
- Registro e consulta de ocorrências com filtros.
- Ordens de serviço corretivas (vinculadas a ocorrência) e preventivas (a partir de plano).
- Manutenção com diagnóstico, serviço realizado, observações e `problemaResolvido` na conclusão.
- Dashboard administrativo.
- Integridade no banco: FKs `Restrict`, índices únicos, CHECK de origem da OS e regras de estado nos serviços.

### Resultados de testes efetivamente executados

- Suíte automatizada: **126/126 aprovados** (108 SQLite/API + 18 contra SQL Server real: 8 da CHECK `CK_OrdensServicos_TipoOrigem` + 10 E2E ponta a ponta em `SistemaManutencaoPIM_E2E_<GUID>`).
- E2E ponta a ponta contra SQL Server LocalDB real (10 casos no arquivo `E2EApiSqlServerTests.cs`): fluxo corretivo completo (ocorrência → OS → assumir → iniciar → manutenção → concluir → ocorrência Resolvida/equipamento Funcionando), fluxo preventivo com e sem resolução (atualização do plano e D2 documentada), e integridade/autorização (409 patrimônio duplicado, 409 ocorrência ativa duplicada, 409 OS duplicada, 400 DTO incorreto, 403 Operador→setor, 403 Técnico→OS, 401 sem JWT).
- Isolamento: a fixture E2E cria `SistemaManutencaoPIM_E2E_<GUID>` exclusivo por execução, sem drop inicial. O cleanup exige ownership após CREATE bem-sucedido e confere nome, `database_id` e `create_date` via `master`; recusa banco preexistente/substituído e também executa em falhas de startup. Os 8 testes da CHECK usam `SistemaManutencaoPIM_ValidationTemp`. Nenhum teste aponta escrita para o banco oficial.
- Validação da API em execução real contra o banco oficial local: health 200, login do Administrador 200, 11 GETs autenticados 200 e controles sem token 401 (Swagger 200).
- Backup pré-migration verificado: `SistemaManutencaoPIM-20261025-020253.bak` com `RESTORE VERIFYONLY` OK, contagens de dados preservadas e hash SHA-256 do Administrador ID 1 inalterado. O arquivo de backup fica fora do repositório, em `C:\Users\andre\AppData\Local\PIMBackups\` (junto do de 21/10/2026), para auditoria posterior.

### Como iniciar e demonstrar

- **Restaurar e compilar:** passo 1 da seção [Instalação e execução](#instalação-e-execução) (`dotnet restore` / `dotnet build`).
- **Migrations:** passo 2 da mesma seção (`dotnet ef database update`).
- **Iniciar a API:** passo 3 (`scripts/iniciar-dev.ps1`), com Swagger em `http://localhost:5088/swagger`.
- **Demonstração guiada:** seção [Testando pelo Swagger](#testando-pelo-swagger) e [coleção Postman](docs/SistemaManutencaoPIM.postman_collection.json) (21 requisições em sequência).

### Como reproduzir o banco

Em base nova, basta `dotnet ef database update --project SistemaManutencao.API`: o repositório contém as migrations que definem todo o esquema (detalhes em [Guia de banco de dados, seção 3](docs/GUIA_BANCO_INTEGRACAO.md)).

### O que depende de Gustavo (React) e Marcos (Flutter)

Consumo da API pelos frontends conforme [docs/CONTRATOS_API.md](docs/CONTRATOS_API.md) e [docs/openapi.json](docs/openapi.json), com apoio do Swagger e da coleção Postman. A integração real ainda **não foi realizada**.

### Decisões pendentes do grupo (D1, D2 e D11)

Em D1 e D2, distinguir dois níveis:

- **Comportamento do DOCX já implementado:** conclusão com `problemaResolvido` `true`/`false` (estados resultantes da ocorrência e do equipamento) e preventiva sem OS automática no MVP.
- **Continuidade operacional ainda não especificada:** o que fazer quando `problemaResolvido = false` (reabertura, nova OS, novo atendimento) e o destino da preventiva não resolvida.

D11 (ocorrência registrada pelo Administrador) aguarda ratificação do grupo. Detalhes em [Pendências e decisões](docs/PENDENCIAS_E_DECISOES.md).

### Limites desta entrega

A entrega cobre o Backend e o Banco de Dados. Não representa aprovação formal do grupo nem homologação de 100% do sistema completo: os frontends React e Flutter ainda não estão integrados, e o banco compartilhado do grupo segue o procedimento da seção 8.3 do [guia de banco de dados](docs/GUIA_BANCO_INTEGRACAO.md).

## Segurança

- O repositório não contém credenciais. Connection string, chave JWT (`Jwt:Key`) e credenciais do seed (`SeedAdmin:Email` e `SeedAdmin:Senha`) são fornecidas por configuração, em variáveis de ambiente ou user-secrets.
- A autenticação usa JWT Bearer com expiração de token, e as permissões são verificadas por perfil em cada rota.

## Estrutura do repositório

```text
SistemaManutencaoPIM/
├── SistemaManutencaoPIM.sln
├── SistemaManutencao.API/
│   ├── Controllers/
│   ├── Services/
│   ├── Models/
│   ├── DTOs/
│   ├── Data/
│   ├── Enums/
│   ├── Helpers/
│   ├── Migrations/
│   └── Program.cs
├── tests/
│   └── SistemaManutencao.Tests/
├── docs/
└── scripts/
    └── iniciar-dev.ps1
```

## Documentação

- [Contratos da API](docs/CONTRATOS_API.md): endpoints, permissões e exemplos JSON.
- [OpenAPI](docs/openapi.json): especificação das operações.
- [Coleção Postman](docs/SistemaManutencaoPIM.postman_collection.json): demonstração por requisições HTTP.
- [DER e modelo físico](docs/DER_MODELO_FISICO.md): diagrama do esquema físico.
- [Dicionário de dados](docs/DICIONARIO_DADOS.md): colunas, tipos, nulabilidade e domínios.
- [Guia de banco de dados](docs/GUIA_BANCO_INTEGRACAO.md): configuração, migrations, backup e handoff.
- [Guia de integração](docs/ENTREGA_EQUIPE.md): orientações para os integrantes da equipe.
- [Roteiro de apresentação](docs/ROTEIRO_APRESENTACAO.md): sequência para demonstrar o projeto.
- [Pendências e decisões](docs/PENDENCIAS_E_DECISOES.md): regras e alinhamentos ainda necessários.

Este repositório contém o **Backend e o Banco de Dados do PIM**. As aplicações React e Flutter e a integração definitiva com o banco do grupo fazem parte das demais etapas do projeto.
