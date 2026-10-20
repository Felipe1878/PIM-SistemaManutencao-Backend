# PIM — Sistema de Manutenção | Entrega da API (André)

API do sistema de manutenção industrial conforme a divisão do `ResumoPIM.docx`: ASP.NET Core Web API, C#, Entity Framework Core, SQL Server, JWT e Swagger. A API é o único acesso ao banco — React (web) e Flutter (mobile) consomem exclusivamente estes endpoints; todas as regras de negócio e transições de estado são centralizadas no backend.

**Estado atual:** build com **0 erros/0 avisos** e **90/90 testes aprovados (44 unitários + 46 HTTP)**. Revisão direcionada: vínculo de equipamento do plano protegido quando existe OS, desafio Bearer restaurado no 401 e esquemas de erro do OpenAPI corrigidos, preservando os envelopes HTTP. Regressão nova contra SQL Server 2022 isolado: **13/13 verificações aprovadas**. O gate SQL anterior e seus limites estão descritos abaixo; decisões de negócio e alinhamento do esquema ainda dependem de aprovação.

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
dotnet test    # 90 testes: unitarios (SQLite) + integracao HTTP (WebApplicationFactory)
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

**Gate histórico (20/10/2026):** SQL Server 2022 (`mcr.microsoft.com/mssql/server:2022-latest`) em contêiner isolado, bind somente em loopback, migrations reais e API persistindo no SQL Server. Registro preciso: primeira sonda **42/43 PASS**; um payload inválido foi corrigido e o re-probe passou **8/8**. Não houve execução única final 43/43. Fluxos resolvido/não resolvido e atribuição concorrente foram observados; isso não é teste de estresse.

- Schema local da API (aprovação de Léo ainda pendente): 7 tabelas; índices únicos com filtros confirmados no banco (`IX_Setores_Nome [Ativo]=(1)`, `IX_Ocorrencias_EquipamentoId [Status]<>(2)`, `IX_OrdensServicos_OcorrenciaId [OcorrenciaId] IS NOT NULL`, patrimônio, email, `Manutencao.OrdemServicoId`); 8 FKs `NO_ACTION` (Restrict).
- Login PBKDF2/JWT real; 401 sem token/com senha errada; 403 por perfil e de técnico não-responsável; 409 de patrimônio/setor duplicados, 2ª ocorrência ativa e 2ª OS para a mesma ocorrência — observados por HTTP; as pré-validações da API podem rejeitar antes das restrições do banco.
- Regra 1:1 confirmada por contagem direta na tabela (1 OS por ocorrência); senhas persistidas somente como hash PBKDF2 (4/4 na verificação direta, zero texto puro).
- Fluxo completo resolvido: OS Concluída + Ocorrência Resolvida + Equipamento Funcionando; não resolvido: OS Concluída + Ocorrência Aberta + Equipamento Parado (Manutencao persistida com datas e `ProblemaResolvido`).
- **Concorrência real**: dois técnicos assumindo a mesma OS simultaneamente → exatamente 1×200 e 1×409 (UPDATE atômico com predicado).

**Regressão direcionada desta revisão:** 13/13 PASS em SQL Server 2022 descartável, com banco novo e migrations reais: bloqueio da troca de equipamento com OS Aberta/EmAtendimento/Concluída, vínculo persistido, edição sem troca permitida, conclusão no equipamento original, desafio Bearer e declaração 409 no OpenAPI. Contêiner/volume e API temporária removidos. Não foram repetidos os testes do gate completo. Os testes de regressão permanentes estão em `HttpPlanoVinculoTests`, `HttpBearerChallengeTests`, `HttpRevogacaoTests` e `HttpOpenApiErrosTests`.

**Limites da evidência histórica:** a sonda de filtro combinado verificava somente status HTTP; a identificação do técnico vencedor usava ordem de retorno das threads, não sua identidade. A observação de uma resposta 200 e outra 409 permanece válida. Confira detalhes em `docs/PENDENCIAS_E_DECISOES.md`.

Reprodução local (Docker):

```powershell
docker run -d --name pim-mssql-gate -p 127.0.0.1:14330:1433 -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=<senha-forte>" -e MSSQL_PID=Developer mcr.microsoft.com/mssql/server:2022-latest
dotnet ef database update --project .\SistemaManutencao.API --connection "Server=127.0.0.1,14330;Database=SistemaManutencaoPIM_GATE;User Id=sa;Password=<senha-forte>;TrustServerCertificate=True"
# inicie a API apontando ConnectionStrings__DefaultConnection para o mesmo banco e execute a coleção Postman ate o fim
```

Pendente para o ambiente do grupo (sem autorização implícita neste README): aplicar `database update` no SQL Server compartilhado (alinhado com Léo) e rodar a coleção Postman ponta a ponta.

## Documentação disponível

- `docs/CONTRATOS_API.md`: endpoints, permissões e exemplos JSON (contrato oficial em texto).
- `docs/openapi.json`: documento OpenAPI exportado do código em execução (28 rotas / 38 operações) — regenerar após mudanças.
- `docs/SistemaManutencaoPIM.postman_collection.json`: demonstração em sequência (21 requisições).
- `docs/ROTEIRO_APRESENTACAO.md`: fala curta para o professor e sequência da demonstração.
- `docs/PENDENCIAS_E_DECISOES.md`: alinhamentos pendentes com Léo, Gustavo e Marcos.
- `tests/SistemaManutencao.Tests/`: 90 testes (unitários + integração HTTP).

## Publicar no GitHub

Publicável como backend com **ressalvas explícitas**, não como entrega funcional sem pendências. D1/D2 (continuidade), D5 (datas informadas pelo técnico), D11/D12 e o alinhamento do modelo com Léo permanecem em `docs/PENDENCIAS_E_DECISOES.md`. Não houve push. `Jwt:Key` e `SeedAdmin` vêm de user-secrets/variáveis de ambiente; senhas em exemplos/fixtures são demonstrativas, não credenciais de produção. Antes de publicar: definir remote/visibilidade e LICENSE, se desejada.

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
├── tests/SistemaManutencao.Tests/   # 90 testes
├── docs/                  # Contratos, OpenAPI, Postman, pendências
└── scripts/iniciar-dev.ps1
```

**Não** inclui telas web, mobile ou um servidor de banco embarcado — o SQL Server é externo à API, conforme a arquitetura pedida no PIM.
