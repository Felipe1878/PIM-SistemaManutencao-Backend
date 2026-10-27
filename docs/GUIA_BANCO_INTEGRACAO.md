# Guia Técnico de Banco de Dados — Configuração, Migrations e Integração

**Projeto:** PIM — Sistema de Manutenção Industrial · **Backend:** ASP.NET Core 8 + EF Core 8.0.11
**Banco oficial:** SQL Server (LocalDB em desenvolvimento) · **Data:** 23/10/2026

Este guia cobre: requisitos, aplicação de migrations em ambiente novo, reprodução do esquema oficial do zero, política de alteração de schema, connection strings (estrutura, sem credenciais), arquitetura de integração dos frontends e backup/restore. Complementa `docs/DER_MODELO_FISICO.md` e `docs/DICIONARIO_DADOS.md`.

---

## 1. Requisitos

| Requisito | Versão/observação |
|---|---|
| .NET SDK | **8.x** (o projeto é `net8.0`) |
| SQL Server | **2022 Express LocalDB** (ou Express/Developer/Developer Edition completa) |
| Ferramenta EF | `dotnet-ef` **8.0.11**, compatível com EF Core 8.0.11 usado no projeto |
| SO | Windows para LocalDB (instalado com o Visual Studio ou via *SQL Server Express LocalDB* standalone) |

Instalar a ferramenta EF (uma vez por máquina):

```powershell
dotnet tool install --global dotnet-ef --version 8.0.11
```

Verificar a instância LocalDB (opcional):

```powershell
sqllocaldb info          # lista instâncias; MSSQLLocalDB é a padrão
sqllocaldb start MSSQLLocalDB
```

---

## 2. Aplicar migrations em ambiente NOVO

Existem dois caminhos, ambos idempotentes: o EF registra as migrations já aplicadas na tabela `__EFMigrationsHistory` e nunca reaplica.

### 2.1 Caminho manual (recomendado para validar o banco antes de abrir a API)

Na raiz do repositório (onde está `SistemaManutencaoPIM.sln`):

```powershell
dotnet restore
dotnet build
dotnet ef database update --project SistemaManutencao.API
```

O comando usa a **`DesignTimeDbContextFactory`** (`SistemaManutencao.API/Data/DesignTimeDbContextFactory.cs`), que resolve o `AppDbContext` para o tooling sem passar pelo `Program.cs` (que exige `Jwt:Key`, inexistente em design-time). A factory lê `ConnectionStrings:DefaultConnection` do `appsettings.json` do projeto e, se não encontrar, cai em um fallback LocalDB local — não é usada em runtime, apenas por `dotnet ef`.

Para conferir o resultado:

```powershell
dotnet ef migrations list --project SistemaManutencao.API
sqlcmd -S "(localdb)\MSSQLLocalDB" -d <NomeBase> -Q "SELECT name FROM sys.tables ORDER BY name;"
```

Esperado: 7 tabelas de negócio (`Usuarios`, `Setores`, `Equipamentos`, `Ocorrencias`, `OrdensServicos`, `Manutencoes`, `PlanosManutencao`) + `__EFMigrationsHistory`.

### 2.2 Caminho automático (fluxo em Development)

Em **Development**, com `appsettings.Development.json` contendo `Database:ApplyMigrationsOnStartup = true`, o `Program.cs` executa na inicialização:

1. `db.Database.MigrateAsync()` — aplica toda migration pendente (na máquina nova: cria o banco e aplica ambas: `InitialCreate` e `OrdemServicoTipoOrigemCheck`);
2. `DbInitializer.CriarAdministradorInicialAsync(...)` — **seed idempotente** do administrador: cria o usuário inicial **somente se a tabela `Usuarios` estiver vazia** (e-mail/senha vêm de configuração `SeedAdmin:*`, nunca hardcoded; o script `scripts/iniciar-dev.ps1` orquestra isso em dev).

Em **produção** (`appsettings.json`) o flag é `false`: migrations são aplicadas por processo controlado (2.1), não no boot da API. Nunca ligue `ApplyMigrationsOnStartup` em produção.

> **⚠️ Risco de migração automática — leia antes de subir a API em Development:**
>
> Com `ASPNETCORE_ENVIRONMENT=Development` e `Database:ApplyMigrationsOnStartup = true`, **qualquer boot da API aplica TODAS as migrations pendentes no banco apontado pela connection string** (`Program.cs:117-127`: `MigrateAsync()` + seed do administrador). No banco oficial local `SistemaManutencaoPIM` as duas migrations já estão aplicadas e validadas (25/10/2026); o risco atual é apontar o boot para qualquer outra base — em especial o banco compartilhado do grupo, ainda não homologado — e migrá-la automaticamente, sem auditoria prévia e sem decisão do grupo.
>
> **Procedimento seguro para iniciar a API SEM migrar:**
>
> ```powershell
> # Variável de ambiente (double-underscore sobrescreve a chave de configuração):
> $env:Database__ApplyMigrationsOnStartup = "false"
> dotnet run --project SistemaManutencao.API
> ```
>
> Alternativas equivalentes: `dotnet run --project SistemaManutencao.API -- --Database:ApplyMigrationsOnStartup=false` (argumento de linha de comando) ou desligar o flag permanentemente em `appsettings.Development.json` enquanto o banco compartilhado do grupo não estiver homologado. Em qualquer caso, aplique migrations de forma **deliberada** com `dotnet ef database update --project SistemaManutencao.API` — no banco oficial local isso já foi feito e validado (25/10/2026); em outras bases, somente **depois** da auditoria pré-aplicação (seção 8.3, passo 4). Nunca aponte o boot em Development para a base do grupo com o flag ligado.

> O seed cria apenas o administrador. Os demais dados (setores, equipamentos, planos) são cadastrados pelos endpoints administrativos.

---

## 3. Reproduzir o esquema oficial do zero

Sequência completa em uma máquina limpa:

```powershell
# 1. Restaurar pacotes
dotnet restore
# 2. Compilar a solução
dotnet build
# 3. (opcional) garantir instância LocalDB
sqllocaldb start MSSQLLocalDB
# 4. Aplicar as migrations (cria o banco e o esquema)
dotnet ef database update --project SistemaManutencao.API
# 5. Subir a API em Development (aplica migrations + seed automaticamente, se habilitado)
.\scripts\iniciar-dev.ps1
```

O esquema resultante é **definido exclusivamente pelas migrations** (`Migrations/20261020021711_InitialCreate.cs` + a incremental `20261023061811_OrdemServicoTipoOrigemCheck`, aplicada e validada no banco oficial local em 25/10/2026, + incrementais futuras). A criação segue a ordem de dependências das FKs: `Setores` → `Usuarios` → `Equipamentos` → `Ocorrencias` → `PlanosManutencao` → `OrdensServicos` → `Manutencoes`. Nenhum script SQL manual faz parte do setup oficial.

> **Testes:** a suíte (xUnit, 126 casos — 108 SQLite/API + 18 de integração SQL Server real) usa SQLite in-memory, `WebApplicationFactory` e LocalDB para comprovar a CHECK no SGBD e os fluxos E2E ponta a ponta — sempre em bancos descartáveis (`SistemaManutencaoPIM_ValidationTemp` na classe `SqlServerTipoOrigemCheckTests` e `SistemaManutencaoPIM_E2E_<GUID>` na suíte `E2EApiSqlServerTests`), nunca no oficial. A validação em SQL Server do grupo é o passo de homologação (seção 8). Os 18 casos SQL Server (trait `Categoria=SqlServer`) exigem LocalDB e **falham sem ela**; para executar apenas a suíte SQLite/API: `dotnet test --filter "Categoria!=SqlServer"`.

---

## 4. Política de alteração de schema — NUNCA altere o banco diretamente

**Proibido:** `ALTER TABLE`, `CREATE INDEX`, `DROP COLUMN` ou qualquer DDL executado direto no banco, fora de migration. O esquema do banco **tem que ser reproduzível a partir do repositório**; qualquer mudança "na mão" diverge o banco do modelo EF e quebra a próxima migration.

Fluxo aprovado (do DOCX do grupo) para qualquer mudança de schema:

1. **Alteração aprovada** — decisão registrada pelo grupo (ex.: CHECK `CK_OrdensServicos_TipoOrigem`, aprovada 23/10/2026);
2. **Model** — ajustar o mapeamento em `Data/AppDbContext.cs` (+ entidade, se necessário) — o snapshot acompanha;
3. **Migration** — `dotnet ef migrations add <Nome> --project SistemaManutencao.API` (gera classe incremental; **nunca editar a `InitialCreate`**);
4. **Revisão** — code review do arquivo gerado (DDL, `Down()`, impacto em dados existentes);
5. **Aplicar** — `dotnet ef database update` (ou boot em Development com o flag ligado);
6. **Testar** — validar integridade no SQL Server real (seção 8) e rodar a suíte.

A CHECK `CK_OrdensServicos_TipoOrigem` seguiu exatamente esse fluxo: a migration incremental (`20261023061811_OrdemServicoTipoOrigemCheck`, classe `OrdemServicoTipoOrigemCheck` com `AddCheckConstraint`/`DropCheckConstraint`) **não toca na `InitialCreate`** — é uma nova classe `Up()/Down()` acrescentando a constraint na tabela `OrdensServicos`; aplicada via `dotnet ef database update` e validada no banco oficial local em 25/10/2026 (o mesmo procedimento da seção 8.3 permanece para o banco compartilhado do grupo).

---

## 5. Connection string por ambiente (estrutura, sem credenciais)

A chave de configuração é `ConnectionStrings:DefaultConnection`. **Este guia documenta apenas a estrutura** — o valor efetivo vem da configuração de cada ambiente e **credenciais/chaves nunca vão para arquivos versionados**.

| Ambiente | Onde fica | Estrutura (placeholders) | Observações |
|---|---|---|---|
| Desenvolvimento (LocalDB) | `appsettings.json` (versionado) | `Server=(localdb)\MSSQLLocalDB;Database=<NomeBase>;Trusted_Connection=True;TrustServerCertificate=True` | Sem senha (Windows auth); nome da base por ambiente |
| Produção / servidor do grupo | variável de ambiente / secret manager | `Server=<HostSqlServer>;Database=<NomeBase>;User Id=<Usuario>;Password=<Senha>;TrustServerCertificate=True` | Prefira `dotnet user-secrets`, variável `ConnectionStrings__DefaultConnection` ou cofre; **nunca** commit |
| Tooling EF (design-time) | `DesignTimeDbContextFactory` | mesma chave do `appsettings.json`, com fallback LocalDB | Somente para `dotnet ef`; não afeta runtime |

Regras:

- `Database=<NomeBase>` deve diferir entre ambientes (ex.: base de dev pessoal vs. base do grupo) para evitar cross-contaminação;
- Em SQL Server autenticado por usuário/senha, injete `User Id`/`Password` por variável de ambiente (`ConnectionStrings__DefaultConnection`), não no JSON versionado;
- A chave JWT (`Jwt:Key`, mínimo 32 bytes) também é configuração de ambiente — o `Program.cs` **recusa iniciar** sem ela; gerada por execução no `iniciar-dev.ps1`.

---

## 6. Integração: React/Flutter → HTTP/JSON → API → EF Core → SQL Server

```text
React (Web) ──────┐
                  ├── HTTP/JSON ──> API ASP.NET Core ──> EF Core 8 ──> SQL Server
Flutter (Mobile) ─┘
```

Princípios invioláveis:

1. **Os frontends NUNCA executam SQL nem conectam ao SQL Server.** Toda leitura/escrita passa pela API REST (JWT Bearer). O banco é acessível apenas pelo host da API.
2. Contratos oficiais: `docs/CONTRATOS_API.md` e `docs/openapi.json` (Swagger em Development: `http://localhost:5088/swagger`). JSON em `camelCase`; enums como texto; datas UTC com sufixo `Z`.
3. Erros: `{ "erro": "mensagem" }` (negócio/401/403) ou `ValidationProblemDetails` (validação de modelo) — os clientes devem tratar os dois formatos, além dos status 400/401/403/404/409/500.
4. Regras de integridade que o frontend verá como **409 Conflict** já estão no banco: e-mail/patrimônio únicos, uma ocorrência ativa por equipamento (índice filtrado), 1:1 ocorrência↔OS, 1:1 OS↔manutenção e — após a migration incremental — a CHECK `CK_OrdensServicos_TipoOrigem`. O frontend não precisa (nem deve) reimplementar essas regras, apenas tratar 409.
5. CORS (browser/React): origens configuradas em `Cors:Origens` (`http://localhost:3000`, `5173`, `8080` em Development). Flutter não é afetado por CORS (não é browser).

---

## 7. Backup e restore (LocalDB) — recomendações

Comandos `sqlcmd` (executar no host do SQL Server). Ajuste `<NomeBase>` e os caminhos:

Backup:

```sql
BACKUP DATABASE [<NomeBase>]
TO DISK = '<CaminhoBackup>\<NomeBase>.bak'
WITH INIT, CHECKSUM;
```

Verificação de integridade do arquivo de backup (sem restaurar):

```sql
RESTORE VERIFYONLY
FROM DISK = '<CaminhoBackup>\<NomeBase>.bak'
WITH CHECKSUM;
```

Restore (em outra máquina/instância):

```sql
RESTORE DATABASE [<NomeBase>]
FROM DISK = '<CaminhoBackup>\<NomeBase>.bak'
WITH REPLACE;
```

Recomendações: fazer backup **após cada rodada de dados importantes** e antes de aplicar migration nova; verificar todo backup com `RESTORE VERIFYONLY` imediatamente após gerá-lo; guardar o backup fora do repositório (nunca versionar `.bak`); em LocalDB, parar a instância (`sqllocaldb stop`) antes de copiar arquivos físicos `.mdf/.ldf` como método alternativo (menos seguro que `BACKUP DATABASE`). **Importante:** restaurar um backup antigo pode rebobinar o `__EFMigrationsHistory` — nesse caso, rodar `dotnet ef database update` para reaplicar migrations pendentes.

---

## 8. Handoff para o grupo

### 8.1 Pendências que permanecem documentadas (não resolvidas aqui)

As decisões **D1**, **D2** e **D11** permanecem abertas em `docs/PENDENCIAS_E_DECISOES.md`, **exatamente como estavam — este conjunto de documentos não as resolve nem altera regras**:

- **D1 — continuidade da corretiva:** comportamento de conclusão com `problemaResolvido = NÃO` e cancelamento (ocorrência reaberta, equipamento Parado, sem endpoint de reabertura) depende de decisão do grupo;
- **D2 — preventiva não resolvida:** OS preventiva concluída deixa o equipamento Parado sem continuidade pelo fluxo atual; regra a definir (sem ocorrência fictícia);
- **D11 — ocorrência pelo administrador:** POST/GET de ocorrências aceitam Administrador e Operador; ratificar ou restringir.

Também permanece válida a nota de compatibilidade com o banco compartilhado: as extensões (`OcorrenciaId` nullable, `PlanoManutencaoId`, `ProblemaResolvido`) e a CHECK nova precisam de aprovação do grupo antes da integração com o banco compartilhado definitivo (ver `docs/DICIONARIO_DADOS.md`, seção 10).

### 8.2 Status da CHECK nova

- **Definição:** `CK_OrdensServicos_TipoOrigem` — corretiva (0) exige `OcorrenciaId NOT NULL` e `PlanoManutencaoId NULL`; preventiva (1) exige `PlanoManutencaoId NOT NULL` e `OcorrenciaId NULL` (SQL completo em `docs/DER_MODELO_FISICO.md` §4 e `docs/DICIONARIO_DADOS.md` §8).
- **Aprovação:** 23/10/2026.
- **Estado:** definida, aprovada e materializada na migration incremental `20261023061811_OrdemServicoTipoOrigemCheck` (nova classe, gerada pelo responsável do banco; **não altera a `InitialCreate`** — migrations antigas nunca são editadas). Aplicada via `dotnet ef database update` e validada no banco oficial local `SistemaManutencaoPIM` em 25/10/2026 (backup prévio verificado, CHECK ativa e confiável — `is_disabled = 0`, `is_not_trusted = 0` —, contagens de dados preservadas). Pendente apenas de aplicação e validação no banco compartilhado do grupo (seção 8.3).
- **Impacto em dados existentes:** qualquer linha de `OrdensServicos` que viole a regra impedirá a aplicação da migration na base alvo — no banco oficial local a aplicação já ocorreu sem impedimentos (25/10/2026); para o banco compartilhado do grupo, rodar a consulta de auditoria antes (seção 8.3, passo 4) e corrigir dados por API/decisão do grupo, nunca por DDL manual.

### 8.3 Passos para o grupo validar em SQL Server próprio

1. **Ambiente isolado:** nunca validar na base compartilhada primeiro. Usar LocalDB própria ou base temporária (ex.: `Database=<NomeBase>_<iniciais>`).
2. **Reproduzir o esquema:** seguir a seção 3 (`dotnet restore` → `build` → `dotnet ef database update`).
3. **Conferir estrutura:** 7 tabelas + `__EFMigrationsHistory`; PKs com identidade; 8 FKs todas `NO ACTION`; 6 índices únicos (3 com filtro: `[Ativo] = 1`, `[Status] <> 2`, `[OcorrenciaId] IS NOT NULL`).
4. **Auditoria pré-CHECK** (antes da migration incremental):
   ```sql
   SELECT Id, TipoManutencao, OcorrenciaId, PlanoManutencaoId
   FROM OrdensServicos
   WHERE NOT ((TipoManutencao = 0 AND OcorrenciaId IS NOT NULL AND PlanoManutencaoId IS NULL)
           OR (TipoManutencao = 1 AND PlanoManutencaoId IS NOT NULL AND OcorrenciaId IS NULL));
   ```
   Zero linhas = seguro aplicar a CHECK.
5. **Aplicar a migration incremental** da CHECK já gerada (`dotnet ef database update`) e confirmar (passo já executado e validado no banco oficial local `SistemaManutencaoPIM` em 25/10/2026; na base do grupo, executar após a auditoria do passo 4):
   ```sql
   SELECT name FROM sys.check_constraints WHERE name = 'CK_OrdensServicos_TipoOrigem';
   ```
6. **Smoke test de integridade:** tentar via Swagger `POST` de OS sem origem nenhuma e uma OS com as duas origens — ambas devem retornar **HTTP 400** pela validação da API. **409** é reservado a conflitos (por exemplo, ocorrência que já possui OS). Uma inserção SQL direta com origem inválida é rejeitada pela CHECK no SQL Server; não se trata de uma resposta HTTP da validação da API.
7. **Fluxo ponta a ponta:** executar o roteiro do README (login → setor → equipamento → ocorrência → OS corretiva → assumir/iniciar → registrar manutenção → concluir com `problemaResolvido` SIM e NÃO) e conferir os estados de ocorrência/equipamento resultantes.
8. **Registrar o resultado** (schema conferido, CHECK aplicada, smoke tests) e comunicar ao responsável pelo SQL Server antes de qualquer promoção para a base do grupo.

---

## 9. Resumo de fontes e regras do repositório

- Fontes técnicas: `Data/AppDbContext.cs`, `Migrations/20261020021711_InitialCreate.cs`, `Data/DesignTimeDbContextFactory.cs`, `Data/DbInitializer.cs`, `Program.cs`, `Enums/Tipos.cs`, `appsettings*.json`, `README.md`.
- Escopo do repositório: somente backend/API. Sem telas React/Flutter; sem DDL manual; chave JWT e senhas nunca versionadas.
- Documentos relacionados: `docs/DER_MODELO_FISICO.md` · `docs/DICIONARIO_DADOS.md` · `docs/CONTRATOS_API.md` · `docs/PENDENCIAS_E_DECISOES.md` · `docs/ENTREGA_EQUIPE.md`.
