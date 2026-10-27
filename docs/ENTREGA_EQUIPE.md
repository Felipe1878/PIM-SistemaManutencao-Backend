# Integração do backend PIM

Guia rápido de contratos e demonstração para a equipe de banco, Gustavo (web) e Marcos (mobile). A especificação detalhada continua em `CONTRATOS_API.md`; a referência interativa está em `/swagger` e a exportação versionada em `openapi.json`. A implementação do banco, assumida pelo responsável do Backend, está **concluída no ambiente local**: as migrations `20261020021711_InitialCreate` e `20261023061811_OrdemServicoTipoOrigemCheck` estão aplicadas e validadas no banco oficial local `SistemaManutencaoPIM` (25/10/2026). Gustavo e Marcos seguem com este guia de integração vigente (contratos, Swagger e coleção Postman).

## Modelo e SQL Server — responsabilidade do Backend

O backend contém sete entidades: `Usuario`, `Setor`, `Equipamento`, `Ocorrencia`, `OrdemServico`, `Manutencao` e `PlanoManutencao`. Relacionamentos: Setor 1:N Equipamento; Equipamento 1:N Ocorrencia e PlanoManutencao; Usuario 1:N Ocorrencia e, como técnico, OS; Ocorrencia 1:1 OS corretiva; PlanoManutencao 1:N OS preventiva; OS 1:1 Manutencao. As FKs usam exclusão restritiva.

Índices relevantes: email único; patrimônio único; nome de setor único filtrado por `Ativo=1`; uma ocorrência ativa por equipamento (índice filtrado por status diferente de Resolvida); `OcorrenciaId` único e filtrado para permitir NULL nas preventivas; `Manutencao.OrdemServicoId` único.

Extensões do modelo backend a alinhar com o esquema do grupo: `OrdemServico.OcorrenciaId` nullable e `PlanoManutencaoId` nullable para representar OS corretiva ou preventiva; `Manutencao.ProblemaResolvido` nullable. Datas de início/conclusão já são campos da entidade `Manutencao`; os DTOs receberam apenas campos opcionais de entrada. A entrega do banco está concluída: as migrations locais existentes são `20261020021711_InitialCreate` e a incremental `20261023061811_OrdemServicoTipoOrigemCheck` (CHECK `CK_OrdensServicos_TipoOrigem`), ambas aplicadas e validadas no banco oficial local em 25/10/2026. Compare as migrations/modelo com o DDL do grupo antes de integrar ao banco compartilhado; aplique apenas a estrutura acordada no SQL Server de integração, sem criar um esquema paralelo incompatível.

## Gustavo — React web

Login: `POST /api/auth/login` recebe `{ "email": "...", "senha": "..." }` e devolve `{ token, tipo: "Bearer", expiraEmUtc, usuario }`. Use `Authorization: Bearer <token>` nas rotas protegidas. O documento `openapi.json` e `/swagger` indicam as permissões; login e `/api/health` são públicos.

Administração inclui usuários (`/api/usuarios`), setores (`/api/setores`), equipamentos (`/api/equipamentos`) e planos (`/api/planos-manutencao`); operações de escrita são restritas ao Administrador. JSON usa `camelCase`, enums em texto, datas UTC. Erros de negócio/autorização usam `{ "erro": "..." }`; validação 400 usa `ValidationProblemDetails.errors`. Filtros: equipamentos `busca`/`incluirInativos`; ocorrências `equipamentoId`, `busca`, `status`; OS `status`; planos `somenteVencidos`/`incluirInativos`.

## Marcos — Flutter mobile

O app autentica em `POST /api/auth/login` (`email`, `senha`) e envia o JWT como Bearer nas rotas protegidas. O Operador consulta `GET /api/ocorrencias/minhas` e registra com `POST /api/ocorrencias` (`equipamentoId`, `descricao`, `prioridade`). A API devolve o ID e põe o equipamento em Parado. As rotas de ocorrência aceitam Administrador e Operador conforme a implementação atual; D11 permanece pendente de ratificação do grupo.

O Técnico consulta OS disponíveis, assume com `POST /api/ordens-servico/{id}/assumir` e inicia em `/iniciar`. Pode enviar `{ "dataInicio": "2026-10-20T10:00:00Z" }`; o corpo é opcional por compatibilidade. Depois, pode registrar diagnóstico/serviço com `PUT /manutencao` e concluir com `POST /concluir`. As datas opcionais `dataInicio` e `dataConclusao` exigem UTC com `Z`; se omitidas, a API grava o horário atual UTC. A conclusão não pode anteceder o início. Se os textos já estiverem registrados, basta enviar `problemaResolvido` e, se desejado, `dataConclusao`; sem registro, informe também `diagnostico` e `servicoRealizado`. `observacoes` omitida é preservada. Somente o técnico responsável pode iniciar, registrar e concluir.

Ao iniciar, OS e equipamento passam a EmAtendimento/EmManutencao; na conclusão, OS fica Concluida. Com problema resolvido, ocorrência fica Resolvida e equipamento Funcionando; sem resolução, ocorrência permanece Aberta e equipamento Parado. D1/D2 continuam sem regra aprovada para a manutenção posterior; esta entrega não reabre OS nem cria segunda OS e preserva o vínculo 1:1.

## Executar e demonstrar localmente

1. Instale/configure .NET 8 e SQL Server LocalDB ou uma instância SQL Server de desenvolvimento. Defina uma connection string para um banco local descartável/de desenvolvimento. Não use o banco compartilhado sem alinhamento com a equipe e o responsável pelo Backend/banco.
2. Na raiz do repositório, execute `dotnet restore`, `dotnet build` e `dotnet test` (126 casos: 108 SQLite/API + 18 contra SQL Server real — LocalDB é necessário).
3. No PowerShell, rode `Set-ExecutionPolicy -Scope Process Bypass` e `./scripts/iniciar-dev.ps1`. Na primeira inicialização, defina a senha de administrador; em Development, com `Database:ApplyMigrationsOnStartup=true`, todas as migrations pendentes são aplicadas, mesmo com tabelas preenchidas. Apenas o seed inicial do Administrador depende de a tabela `Usuarios` estar vazia.
4. Abra `http://localhost:5088/swagger`. O fluxo de demonstração é: login admin → criar setor/equipamento e usuários → login operador → registrar ocorrência → admin cria OS → login técnico → assumir/iniciar → registrar manutenção → concluir → consultar OS, ocorrência e equipamento.
5. Para executar a sequência automatizada de demonstração, importe `SistemaManutencaoPIM.postman_collection.json` no Postman e configure as variáveis de ambiente descritas na coleção.

Decisões ainda pendentes e limites de integração estão em `PENDENCIAS_E_DECISOES.md`.
