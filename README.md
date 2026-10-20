# PIM — Sistema de Manutenção | Entrega da API (André)

Base implementada conforme a divisão do `ResumoPIM.docx`: Backend ASP.NET Core Web API, C#, Entity Framework Core, SQL Server, JWT e Swagger. CRUD de usuários, setores, equipamentos, ocorrências, OS e plano preventivo; 7 tabelas com migration inicial; regras de status, permissões e vínculo 1:1. **Projeto-fonte preparado, mas NÃO compilado nem executado neste ambiente (sem .NET SDK ou SQL Server disponíveis).** Para entrega acadêmica, execute no Windows e registre o teste no Swagger.

## O que instalar

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (não apenas Runtime)
- SQL Server LocalDB (incluído em algumas instalações de Visual Studio), ou SQL Server Express / Developer.
- VS Code com extensão C# ou Visual Studio (opcional).
- Conexão à internet na primeira restauração dos pacotes NuGet.

## Caminho mais rápido no Windows (PowerShell)

1. Descompacte a pasta `SistemaManutencaoPIM` em Documentos.
2. Abra um terminal PowerShell **na pasta principal** (a que contém `scripts` e `SistemaManutencao.API`).
3. Execute:

```powershell
 dotnet --version
 Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
 .\scripts\iniciar-dev.ps1
```

O script solicita a senha inicial, define email `admin@pim.local`, gera uma **chave JWT temporária aleatória** (vale para essa sessão) e inicia a API no endereço `http://localhost:5088/swagger`.

> A migration inicial é aplicada automaticamente **somente em Development**. Se SQL Server LocalDB não estiver disponível, configurar `ConnectionStrings__DefaultConnection` no PowerShell, usando um SQL Server instalado. A senha inicial é usada **somente** se a tabela Usuarios ainda estiver vazia. Para reiniciar a demonstração use a mesma senha, pois o banco guarda o hash.

## Se aparecerem erros

- `'dotnet' não é reconhecido`: falta instalar o SDK, ou reiniciar o terminal.
- `LocalDB` ou instância não encontrada: instale SQL Server Express LocalDB, ou altere a connection string para uma instância SQL Server em execução.
- Falha `NU1301`/NuGet: internet indisponível, fontes NuGet bloqueadas ou falta restaurar dependências.
- `Login failed`: corrigir a string de conexão, autenticação do SQL Server e permissões.
- `Jwt:Key`: iniciar pelo script (ele cria a chave), ou configurar via `dotnet user-secrets set "Jwt:Key" "SUA_CHAVE_LONGA_32_BYTES_MINIMO" --project .\SistemaManutencao.API`.
- Usuário admin não é criado: banco já tem usuário ou `SeedAdmin__Senha` não foi definido na primeira inicialização.

## Teste básico no Swagger

1. POST `/api/auth/login`: `{"email":"admin@pim.local","senha":"senha_digitada_no_script"}`.
2. Copie o `token` da resposta; clique em **Authorize** no Swagger e cole apenas o token.
3. POST `/api/setores`: `{"nome":"Producao"}`.
4. POST `/api/equipamentos`: `{"nome":"Prensa 01","patrimonio":"PAT-001","setorId":1}`.
5. POST `/api/usuarios` duas vezes, criando um Operador e um Tecnico (veja `docs/CONTRATOS_API.md`).
6. Login como Operador. POST `/api/ocorrencias` para equipamento 1.
7. Login de Admin. POST `/api/ordens-servico` para a ocorrência criada.
8. Login como Tecnico. POST `/api/ordens-servico/1/assumir`, POST `/iniciar`, PUT `/manutencao` (opcional antes de concluir), POST `/concluir` com diagnóstico e `problemaResolvido: true`.
9. GET `/api/equipamentos/1` para ver `status: "Funcionando"` e GET `/api/ocorrencias/1` para ver `"Resolvida"`.
10. Repita com outro equipamento e `problemaResolvido: false` para testar a regra contrária.

Os IDs apresentados são exemplos; use os IDs reais retornados nas respostas. Quando trocar de usuário, substitua o token no botão Authorize.

## Documentação disponível

- `docs/CONTRATOS_API.md`: todos os endpoints, permissões e exemplos JSON.
- `docs/SistemaManutencaoPIM.postman_collection.json`: coleção Postman com demonstração em sequência. Importar e ajustar a variável `adminSenha`.
- `docs/ROTEIRO_APRESENTACAO.md`: fala curta para o professor e sequência da demonstração.
- `docs/PENDENCIAS_E_DECISOES.md`: pontos que dependem de alinhamento com Léo, Gustavo e Marcos.
- `SistemaManutencao.API/Migrations/`: migration SQL Server e snapshot.
- `tests/SistemaManutencao.Tests/`: testes unitários de validação de perfil, comportamento de estados e de fluxos com banco em memória SQLLite (executar após restaurar pacotes).

## Rodar testes

```powershell
 dotnet test .\tests\SistemaManutencao.Tests\SistemaManutencao.Tests.csproj
```

## Estrutura

```
SistemaManutencaoPIM/
├── SistemaManutencao.API/
│   ├── Controllers/       # HTTP e permissões
│   ├── Services/          # Login, ocorrência, OS e regras de negócio
│   ├── Models/            # 7 entidades
│   ├── DTOs/              # Contratos e validações
│   ├── Data/              # DbContext e primeiro administrador
│   ├── Enums/             # Perfis, status e tipos
│   ├── Helpers/           # Erros e identificação
│   ├── Migrations/        # Estrutura versionada do banco
│   └── Program.cs         # JWT, DI, Swagger e configuração
├── tests/
├── docs/
└── scripts/
```

**Não** inclui telas web, mobile ou um servidor de banco embarcado. O SQL Server é externo à API, conforme arquitetura pedida no PIM.
