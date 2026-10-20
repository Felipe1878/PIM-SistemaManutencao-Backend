# Contratos da API para React e Flutter

URL padrão em desenvolvimento: `http://localhost:5088`. Documentação Swagger: `/swagger`.
As respostas usam JSON e propriedades em `camelCase`. Os enums são representados como textos (`"Aberta"`, `"Parado"`, `"Tecnico"`); acentos foram removidos apenas nos identificadores de código. Use `Authorization: Bearer <token>` nos endpoints protegidos.

| Método | Endpoint | Quem usa | Função |
|---|---|---|---|
| POST | `/api/auth/login` | Todos | Login, retorna JWT |
| GET | `/api/auth/me` | Logados | Perfil logado |
| GET | `/api/usuarios` | Admin | Lista usuários, sem senhas |
| GET | `/api/usuarios/{id}` | Admin | Consulta |
| POST | `/api/usuarios` | Admin | Cadastro |
| PUT | `/api/usuarios/{id}` | Admin | Atualiza perfil e nome |
| PATCH | `/api/usuarios/{id}/inativar` | Admin | Inativa usuário |
| GET | `/api/setores` | Logados | Lista |
| GET | `/api/setores/{id}` | Logados | Consulta |
| POST | `/api/setores` | Admin | Cadastro |
| PUT | `/api/setores/{id}` | Admin | Atualiza |
| PATCH | `/api/setores/{id}/inativar` | Admin | Inativa |
| GET | `/api/equipamentos?busca=Prensa` | Logados | Lista/pesquisa |
| GET | `/api/equipamentos/{id}` | Logados | Consulta |
| POST | `/api/equipamentos` | Admin | Cadastro |
| PUT | `/api/equipamentos/{id}` | Admin | Atualiza dados, não muda status |
| PATCH | `/api/equipamentos/{id}/inativar` | Admin | Inativa |
| GET | `/api/ocorrencias` | Logados | Lista e filtro equipamentoId |
| GET | `/api/ocorrencias/{id}` | Logados | Consulta |
| POST | `/api/ocorrencias` | Admin/Operador | Registra ocorrência e para equipamento |
| GET | `/api/ordens-servico` | Logados | Lista, filtro status |
| GET | `/api/ordens-servico/{id}` | Logados | Detalhes e manutenção |
| GET | `/api/ordens-servico/disponiveis` | Logados | OS sem técnico |
| GET | `/api/ordens-servico/minhas` | Técnico | OS assumidas |
| POST | `/api/ordens-servico` | Admin | Cria corretiva ou preventiva manual |
| POST | `/api/ordens-servico/{id}/assumir` | Técnico | Reserva a OS |
| POST | `/api/ordens-servico/{id}/iniciar` | Técnico | Começa serviço e põe equipamento em manutenção |
| PUT | `/api/ordens-servico/{id}/manutencao` | Técnico | Registra diagnóstico e serviço durante atendimento |
| POST | `/api/ordens-servico/{id}/concluir` | Técnico | Registra diagnóstico e fecha OS |
| POST | `/api/ordens-servico/{id}/cancelar` | Admin | Cancela OS aberta |
| GET | `/api/planos-manutencao` | Logados | Lista; `somenteVencidos=true` |
| GET | `/api/planos-manutencao/{id}` | Logados | Consulta |
| POST | `/api/planos-manutencao` | Admin | Cadastro |
| PUT | `/api/planos-manutencao/{id}` | Admin | Atualização |
| PATCH | `/api/planos-manutencao/{id}/inativar` | Admin | Inativa |
| GET | `/api/dashboard` | Admin | Contadores para React |
| GET | `/api/health` | Público | Verificação simples |

## JSON exemplos

Login: `POST /api/auth/login`
```json
{"email":"admin@pim.local","senha":"SENHA_ESCOLHIDA"}
```
Usuário operador (Admin): `POST /api/usuarios`
```json
{"nome":"Maria Operadora","email":"operador@pim.local","senha":"SenhaForte123!","perfil":"Funcionario","funcao":"Operador"}
```
Usuário técnico (Admin): `POST /api/usuarios`
```json
{"nome":"Joao Tecnico","email":"tecnico@pim.local","senha":"SenhaForte123!","perfil":"Funcionario","funcao":"Tecnico"}
```
Setor (Admin): `POST /api/setores`
```json
{"nome":"Producao"}
```
Equipamento (Admin): `POST /api/equipamentos`
```json
{"nome":"Prensa 01","patrimonio":"PAT-001","fabricante":"Fabricante X","modelo":"P100","numeroSerie":"S123","setorId":1}
```
Ocorrência (Operador): `POST /api/ocorrencias`
```json
{"equipamentoId":1,"descricao":"Equipamento nao liga apos acionamento.","prioridade":"Alta"}
```
OS corretiva (Admin): `POST /api/ordens-servico`
```json
{"tipoManutencao":"Corretiva","ocorrenciaId":1}
```
OS preventiva manual (Admin): `POST /api/ordens-servico`
```json
{"tipoManutencao":"Preventiva","planoManutencaoId":1}
```
Concluir (Técnico): `POST /api/ordens-servico/1/concluir`
```json
{"diagnostico":"Cabo de alimentacao rompido.","servicoRealizado":"Cabo substituido e funcionamento verificado.","observacoes":"Teste realizado com sucesso.","problemaResolvido":true}
```
Plano preventivo (Admin): `POST /api/planos-manutencao`
```json
{"equipamentoId":1,"descricao":"Inspecao mensal de lubrificacao","periodicidadeDias":30,"proximaExecucao":"2026-11-15T09:00:00Z"}
```

## Regras de erro
- `400`: JSON inválido, validação ou fluxo de criação malformado.
- `401`: login inválido, token inexistente/expirado ou conta inativada.
- `403`: perfil sem autorização, ou técnico diferente do responsável.
- `404`: identificador não encontrado.
- `409`: operação em conflito com outra ocorrência/OS, estado, nome ou patrimônio duplicado.
- `500`: erro inesperado. Detalhes ficam nos logs, não são exibidos para o usuário.
