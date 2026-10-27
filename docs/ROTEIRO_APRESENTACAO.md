# Como apresentar sua parte (André — Backend)

**Frase inicial:** "Minha responsabilidade foi desenvolver a API em C# com ASP.NET Core. Ela liga as telas em React e Flutter ao SQL Server sem permitir acesso direto ao banco e centraliza as regras de negócio."

**1. Arquitetura:** React/Flutter → requisição HTTP/JSON → Controller → Service → Entity Framework Core → SQL Server. A API responde JSON.

**2. Segurança:** "O login recebe email/senha, compara o hash da senha e devolve um JWT. Cada endpoint exige a permissão apropriada: administrador, operador ou técnico."

**3. Regra do equipamento:** "Quando o operador registra uma ocorrência, o backend confere se já existe ocorrência ativa e muda o equipamento de Funcionando para Parado. É impossível ter duas ocorrências ativas no mesmo equipamento."

**4. Fluxo de OS:** "O administrador cria a OS sem técnico. Um técnico pode assumir, iniciar a manutenção e depois concluir com diagnóstico e serviço. A API controla todos os estados."

**5. Dois desfechos:** "Se o problema foi resolvido, a OS fica concluída, a ocorrência resolvida e o equipamento funcionando. Se não resolveu, a OS fica concluída, a ocorrência continua aberta e o equipamento parado."

**6. Prevenção:** "O administrador acompanha os planos e cria a OS manualmente. O MVP não cria OS preventivas automaticamente."

**Demonstração sugerida:** abrir Swagger, login de admin, criar setor, equipamento, operador e técnico, login do operador, registrar ocorrência, admin criar OS, login do técnico, assumir, iniciar, concluir e conferir o status do equipamento. Usar senhas e tokens próprios do ambiente de teste.

**Se perguntarem o que falta:** "A base da API está implementada, compilada e homologada: a suíte roda com 126 casos aprovados e as migrations já foram aplicadas e validadas no banco oficial local. O que falta é a homologação no banco compartilhado do grupo e a integração pelos frontends React e Flutter."
