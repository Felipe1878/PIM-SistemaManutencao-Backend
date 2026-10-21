# Sistema de Manutenção Industrial — API

**Backend REST API | ASP.NET Core 8**

API desenvolvida como parte de um Projeto Integrado Multidisciplinar (PIM), responsável pelo gerenciamento de equipamentos, ocorrências e ordens de serviço de manutenção industrial.

O backend centraliza a autenticação, as regras de negócio e o acesso aos dados. As aplicações web (React) e mobile (Flutter), desenvolvidas pelos demais integrantes da equipe, utilizam seus endpoints HTTP.

Este repositório contém exclusivamente a **API do projeto**, incluindo código-fonte, migrations, testes e documentação de integração.

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
| Entity Framework Core | Persistência, relacionamentos e migrations |
| SQL Server | Banco de dados relacional |
| JWT Bearer | Autenticação e autorização |
| Swagger / OpenAPI | Documentação dos endpoints |
| Postman | Demonstração dos fluxos HTTP |
| xUnit / WebApplicationFactory / SQLite | Testes automatizados |

A API mantém as regras de negócio nos serviços, enquanto os controllers recebem as requisições HTTP e os DTOs definem os contratos de entrada e saída.

```text
React (Web) ──────┐
                  ├── HTTP/JSON ──> API ASP.NET Core ──> EF Core ──> SQL Server
Flutter (Mobile) ─┘
```

Os clientes não acessam o banco diretamente. Toda comunicação com os dados passa pela API.

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

### 2. Iniciar a API

No Windows, utilizando PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\iniciar-dev.ps1
```

O script configura o ambiente de desenvolvimento e utiliza `admin@pim.local` como e-mail padrão do administrador inicial.

Se `SeedAdmin__Senha` não estiver configurada, solicita uma senha. O administrador inicial só é cadastrado quando a tabela `Usuarios` está vazia.

Se `Jwt__Key` não estiver definida, o script gera uma chave aleatória para a execução. Tokens gerados anteriormente deixam de funcionar quando a API é reiniciada com uma chave diferente.

Em Development, as migrations são aplicadas automaticamente conforme a configuração do projeto. É necessário ter uma instância SQL Server acessível.

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
Authorization: Bearer <token>
```

Para consultar os dados do usuário logado:

```http
GET /api/auth/me
Authorization: Bearer <token>
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

A API possui testes unitários e testes de integração HTTP, utilizando SQLite em memória e `WebApplicationFactory`.

O último resultado reportado pelo Astra foi:

- **106/106 testes aprovados, 0 falhas e 0 ignorados.**
- Compilação com 0 erros e 0 avisos.

Esses resultados pertencem à rodada de qualidade anterior do Astra; nenhuma nova execução foi realizada nesta atualização documental.

### Validação histórica com SQL Server

Também foram realizadas verificações anteriores em SQL Server 2022, utilizando um banco temporário isolado.

O histórico registra:

- Primeira execução com 42/43 verificações aprovadas.
- Correção de um payload da sonda, seguida de reexecução direcionada com 8/8 aprovações.
- Regressão posterior com 13/13 verificações aprovadas.

Essas validações abrangeram fluxos de manutenção, integridade dos relacionamentos, restrições de unicidade, autenticação e atribuição concorrente de ordens de serviço.

Não houve uma execução única final de 43/43 verificações, nem teste de carga.

A integração da versão final com o SQL Server definitivo do grupo e a demonstração ponta a ponta nesse ambiente ainda precisam ser realizadas após o alinhamento do esquema.

## Integração com a equipe

Este repositório entrega a API utilizada pelos demais componentes do PIM.

| Área | Responsabilidade |
|---|---|
| Banco de dados | Compatibilizar o esquema SQL Server e seus relacionamentos com a API |
| Frontend React | Consumir os endpoints administrativos, autenticação e dashboard |
| Aplicativo Flutter | Consumir login, ocorrências e fluxos de atendimento de OS |

A API utiliza suas próprias entidades e migrations. Antes da integração definitiva, o esquema precisa ser comparado com o modelo acordado pela equipe, incluindo chaves estrangeiras, índices e campos opcionais.

O guia de integração está disponível em:

[Integração do backend com a equipe](docs/ENTREGA_EQUIPE.md)

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
- [Guia de integração](docs/ENTREGA_EQUIPE.md): orientações para os integrantes da equipe.
- [Roteiro de apresentação](docs/ROTEIRO_APRESENTACAO.md): sequência para demonstrar o projeto.
- [Pendências e decisões](docs/PENDENCIAS_E_DECISOES.md): regras e alinhamentos ainda necessários.

Este repositório contém somente o **Backend/API do PIM**. As aplicações React e Flutter, o banco compartilhado e sua integração definitiva fazem parte das demais etapas do projeto em grupo.