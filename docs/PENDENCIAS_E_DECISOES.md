# Decisões pendentes e limites de integração

Revisão direcionada do código e do `ResumoPIM.docx` em 20/10/2026. Os testes demonstram o comportamento implementado, não substituem aprovação das regras abaixo.

## Decisões que exigem aprovação

- **D1 — continuidade da corretiva:** `ConcluirAsync(false)` conclui a OS, reabre a ocorrência e deixa o equipamento Parado. `CancelarAsync` também mantém a ocorrência ativa. `CriarAsync` rejeita outra OS para essa ocorrência (1:1); `RegistrarAsync` rejeita outra ocorrência ativa. Não há endpoint de reabertura. Definir continuidade sem remover a cardinalidade oficial.
- **D2 — preventiva não resolvida:** conclui a OS e deixa o equipamento Parado, sem ocorrência vinculada. Nova preventiva exige equipamento Funcionando; nova ocorrência também exige Funcionando. O equipamento fica sem continuidade pelo fluxo atual. Definir a regra, sem criar ocorrência fictícia ou mudar status silenciosamente.
- **D5 — datas da manutenção (implementado):** `/iniciar` aceita `dataInicio` UTC opcional e `/concluir` aceita `dataConclusao` UTC opcional. O servidor mantém compatibilidade e usa `DateTime.UtcNow` quando omitidas. Rejeita valores sem UTC e conclusão anterior ao início. Nenhuma coluna/migration foi alterada.
- **D11 — ocorrência pelo administrador:** hoje POST e GET de ocorrências aceitam Administrador/Operador. O Word atribui registro ao Operador e acompanhamento ao Administrador, sem explicitar essa permissão adicional. Ratificar ou restringir; não foi alterada nesta revisão.
- **D12 — registro versus conclusão (implementado):** PUT `/manutencao` permanece opcional; a conclusão reutiliza diagnóstico/serviço já registrados ou aceita ambos no próprio POST. Observações omitidas são preservadas; técnico responsável continua obrigatório. Nenhuma migration foi alterada.

## Compatibilidade com Léo

Comparação feita com a seção de banco do Word; nenhum DDL/modelo executável de Léo foi encontrado no workspace. Portanto, compatibilidade com o banco efetivo do grupo **não está certificada**.

- `OrdemServico.PlanoManutencaoId` opcional e `OcorrenciaId` nullable são extensões do backend para preventiva sem ocorrência. Precisam de aprovação antes da integração; preservam o índice único de OcorrenciaId e a relação 1:1.
- `Manutencao.ProblemaResolvido` nullable é extensão necessária ao resultado SIM/NÃO e precisa ser alinhada.
- Confirmar também Email UNIQUE, índices únicos filtrados (setor ativo/ocorrência ativa), nulabilidades, ações restritivas das FKs e convenção de datas UTC. Migrations locais passarem não prova aprovação do esquema compartilhado.
- Nenhuma migration foi modificada ou aplicada ao banco do grupo nesta revisão.

## Evidência e publicação

- Build desta entrega: 0 erros/0 avisos; suíte: 94/94 testes (44 unitários + 50 HTTP), executados localmente sobre SQLite in-memory/WebApplicationFactory. OpenAPI exportado do servidor local com banco não acessado. As verificações SQL Server abaixo são históricas e não foram repetidas nesta entrega.
- O gate histórico teve 42/43 verificações no primeiro script; a falha era payload inválido da sonda e o re-probe corrigido passou 8/8. Não houve uma execução única final 43/43. A sonda de filtro combinado verificava apenas os status HTTP, não o conteúdo; a escolha do técnico vencedor dependia indevidamente da ordem de conclusão das threads. A observação de uma resposta 200 e outra 409 permanece válida, mas não equivale a teste de estresse.
- Login de email inexistente executa PBKDF2 fictício para mitigar o caminho rápido; não há comprovação de tempo constante nem de eliminação completa de canal de timing.
- Publicável como backend com limitações declaradas. Não apresentar como solução de negócio integralmente aprovada antes das decisões acima e do alinhamento do esquema.

Escopo exclusivo de André: sem telas React/Flutter e sem mudanças no banco compartilhado. Autenticação inicial continua por SeedAdmin somente com tabela de usuários vazia; chave JWT via configuração segura, nunca literal de produção no repositório.
