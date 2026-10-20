# PIM — Sistema de Manutenção | Entrega da API (André)

API do sistema de manutenção industrial conforme a divisão do `ResumoPIM.docx`: ASP.NET Core Web API, C#, Entity Framework Core, SQL Server, JWT e Swagger. A API é o único acesso ao banco — React (web) e Flutter (mobile) consomem exclusivamente estes endpoints; todas as regras de negócio e transições de estado são centralizadas no backend.

**Estado atual:** compilado com 0 erros/0 avisos; **78 testes automatizados verdes (44 unitários + 34 de integração HTTP)**, executados 2× com determinismo; smoke HTTP real validado (health, 401 com corpo JSON, CORS preflight/origem permitida/origem bloqueada). **Pendente de ambiente:** fluxo completo contra SQL Server real — esta máquina não possui LocalDB/SQL Server (ver "Validação com SQL Server real").

## O que instalar

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — o SDK 10 também compila este projeto net8.0 (comprovado).
- Ferramenta do EF para migrations: `dotnet tool install --global dotnet-ef --version 8.0.11`.
- SQL Server LocalDB (vem com o Visual Studio), ou SQL Server Express / Developer — necessário para rodar com banco real.
- VS Code com extensão C# ou Visual Studio (opcional).
- Internet na primeira restauração dos pacotes NuGet.

## Caminho mais rápido no Windows (PowerShell)

1. Abra um terminal PowerShell **na pasta principal** (a que contém o `.sln`).
2. Execute:

```powershell
dotnet --version
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\iniciar-dev.ps1
```

O script solicita a senha inicial (usada **somente** se a tabela `Usuarios` estiver vazia), define o email `admin@pim.local`, gera uma **chave JWT aleatória por sessão** e abre a API em `http://localhost:5088/swagger`.

> A migration inicial é aplicada automaticamente **somente em Development** e requer SQL Server/LocalDB disponível. Sem LocalDB, configure `ConnectionStrings__DefaultConnection` apontando para um SQL Server existente.

## Build e testes (na raiz, via .sln)

```powershell
dotnet restore
dotnet build
dotnet test    # 78 testes: unitarios (SQLite) + integracao HTTP (WebApplicationFactory)
```

## Contratos que o frontend precisa respeitar

- **Campos obrigatórios (enum):** `perfil` (POST/PUT `/api/usuarios`), `prioridade` (POST `/api/ocorrencias`) e `tipoManutencao` (POST `/api/ordens-servico`) — omissão ou `null` devolve `400` com detalhe por campo. Nunca omita: a omissão de `perfil` já criou silenciosamente um Administrador no passado.
- **Permissões por perfil** (conforme o ResumoPIM): ocorrências (leitura/POST) → Admin/Operador; OS (leitura) → Admin/Técnico; assumir/iniciar/registrar/concluir → Técnico; criar/cancelar OS, planos, usuários, setores, escrita de equipamentos e dashboard → Admin; consulta de equipamentos/setores → qualquer autenticado. Matriz completa em `docs/CONTRATOS_API.md`, `docs/openapi.json` e no Swagger.
- **`GET /api/ocorrencias/minhas`** (Admin/Operador): ocorrências registradas pelo usuário logado — tela inicial do Operador no mobile.
- **Filtros de ocorrências:** `?busca=` (texto na descrição ou nome do equipamento), `?status=`, `?equipamentoId=` (combináveis).
- **Erros:** negócio/401/403 → envelope `{"erro":"mensagem"}` (inclusive corpos de 401/403 do middleware JWT); validação 400 → `ValidationProblemDetails` com `errors` por campo — trate ambos no cliente.
- **CORS:** em Development libera `http://localhost:3000`, `http://localhost:5173` e `http://localhost:8080` (`Cors:Origens`); em produção configure `Cors:Origens` com as origens reais — sem a chave, nenhuma origem é liberada.
- **Datas em UTC** — converter para o fuso local na exibição.

## Teste básico no Swagger

1. POST `/api/auth/login`: `{"email":"admin@pim.local","senha":"senha_digitada_no_script"}`.
2. Copie o `token`; em **Authorize**, cole apenas o token.
3. POST `/api/setores`: `{"nome":"Producao"}`.
4. POST `/api/equipamentos`: `{"nome":"Prensa 01","patrimonio":"PAT-001","setorId":1}`.
5. POST `/api/usuarios` duas vezes: um Operador e um Técnico (exemplos em `docs/CONTRATOS_API.md`).
6. Login como Operador. POST `/api/ocorrencias` (`prioridade` obrigatória). GET `/api/ocorrencias/minhas`.
7. Login de Admin. POST `/api/ordens-servico` (`tipoManutencao` obrigatório) para a ocorrência criada.
8. Login como Técnico. POST `/{id}/assumir`, POST `/{id}/iniciar`, PUT `/{id}/manutencao` (opcional), POST `/{id}/concluir` com `problemaResolvido: true`.
9. Login de Admin. GET `/api/equipamentos/{id}` → `"Funcionando"`; GET `/api/ocorrencias/{id}` → `"Resolvida"`.
10. Repita com outro equipamento e `problemaResolvido: false` (OS Concluída, ocorrência segue Aberta, equipamento Parado).

IDs acima são exemplos; use os IDs reais retornados nas respostas.

## Se aparecerem erros

- `'dotnet' não é reconhecido`: instalar o SDK ou reiniciar o terminal.
- `LocalDB`/instância não encontrada: instalar SQL Server Express LocalDB ou configurar `ConnectionStrings__DefaultConnection` para um SQL Server existente.
- `NU1301`/NuGet: internet indisponível, fontes NuGet bloqueadas ou falta `dotnet restore`.
- `Login failed`: revisar string de conexão, autenticação do SQL Server e permissões.
- `Jwt:Key`: iniciar pelo script (gera a chave) ou `dotnet user-secrets set "Jwt:Key" "SUA_CHAVE_LONGA_32_BYTES_MINIMO" --project .\SistemaManutencao.API`.
- Admin não é criado: banco já tem usuários ou `SeedAdmin__Senha` não foi definido na primeira execução.
- `dotnet ef` não encontrado: `dotnet tool install --global dotnet-ef --version 8.0.11`.

## Migrations (tooling padrão)

```powershell
dotnet ef migrations list --project .\SistemaManutencao.API
dotnet ef migrations add NomeDaMudanca --project .\SistemaManutencao.API
dotnet ef database update --project .\SistemaManutencao.API
```

Mudanças estruturais seguem o protocolo do grupo: alinhar com Léo antes de aplicar.

## Validação com SQL Server real

Os testes automatizados rodam sem SQL Server (SQLite in-memory + WebApplicationFactory). Antes da apresentação, com SQL Server disponível: `dotnet ef database update` + executar a coleção Postman (`docs/SistemaManutencaoPIM.postman_collection.json`, 20 requisições) do início ao fim.

## Documentação disponível

- `docs/CONTRATOS_API.md`: endpoints, permissões e exemplos JSON (contrato oficial em texto).
- `docs/openapi.json`: documento OpenAPI exportado do código em execução (28 rotas / 38 operações) — regenerar após mudanças.
- `docs/SistemaManutencaoPIM.postman_collection.json`: demonstração em sequência (20 requisições).
- `docs/ROTEIRO_APRESENTACAO.md`: fala curta para o professor e sequência da demonstração.
- `docs/PENDENCIAS_E_DECISOES.md`: alinhamentos pendentes com Léo, Gustavo e Marcos.
- `tests/SistemaManutencao.Tests/`: 78 testes (unitários + integração HTTP).

## Publicar no GitHub

Esta pasta é um repositório Git pronto (commits locais; **push não realizado**). Nenhum segredo é versionado: `Jwt:Key` e `SeedAdmin` vêm de user-secrets/variáveis de ambiente; a connection string usa `Trusted_Connection`. Antes do push: revisar `docs/`, definir LICENSE (decisão sua) e adicionar o remote.

## Estrutura

```
SistemaManutencaoPIM/
├── SistemaManutencaoPIM.sln
├── SistemaManutencao.API/
│   ├── Controllers/       # HTTP, permissões e contratos de resposta
│   ├── Services/          # Login, ocorrência, OS e regras de negócio
│   ├── Models/            # 7 entidades
│   ├── DTOs/              # Contratos de entrada/saída e validações
│   ├── Data/              # DbContext, seed do admin e fábrica de design-time
│   ├── Enums/             # Perfis, status e tipos
│   ├── Helpers/           # Erros de negócio e identidade do usuário logado
│   ├── Migrations/        # Estrutura versionada do banco (tooling padrão)
│   └── Program.cs         # JWT, CORS, DI, tratamento de erros, Swagger
├── tests/SistemaManutencao.Tests/   # 78 testes
├── docs/                  # Contratos, OpenAPI, Postman, pendências
└── scripts/iniciar-dev.ps1
```

**Não** inclui telas web, mobile ou um servidor de banco embarcado — o SQL Server é externo à API, conforme a arquitetura pedida no PIM.
