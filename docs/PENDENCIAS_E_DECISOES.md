# Pontos de integração a alinhar com Léo, Gustavo e Marcos

1. **OS preventiva:** o documento original define `OrdemServico.OcorrenciaId` e, ao mesmo tempo, diz que o administrador pode criar OS preventiva manualmente. Para suportar OS preventiva sem gerar ocorrência falsa, este backend inclui `PlanoManutencaoId` **opcional** em OrdemServico e faz `OcorrenciaId` opcional **somente quando a OS for preventiva**. O banco precisa aprovar a extensão e a regra de integridade antes do merge. Isso não altera a regra 1:1 entre ocorrência e OS corretiva.
2. **ProblemaResolvido:** como a conclusão precisa registrar SIM/NÃO, foi adicionado `Manutencao.ProblemaResolvido` nullable, por estar em branco até conclusão. Alinhar com o banco.
3. **OS corretiva concluída sem resolução:** fiel ao documento, o registro fica com status `Concluida` e a ocorrência continua ativa (`Aberta`). Porém, devido à regra de **apenas uma OS por ocorrência**, fica faltando uma decisão para uma nova tentativa (reabrir a mesma OS? novo vínculo? ocorrência de acompanhamento?). O MVP não faz reabertura automática.
4. **Cancelamento de OS vinculada:** cancelar OS corretiva mantém ocorrência aberta; devido à relação 1:1, não é possível criar segunda OS para ela. Definir com o grupo se deve permitir reabrir a mesma OS cancelada.
5. **Autenticação:** credencial inicial somente quando o banco ainda não possui usuários e `SeedAdmin` está configurado; a senha não fica no repositório. A API exige chave JWT de pelo menos 32 bytes.
6. **Data/hora:** grava UTC. A interface deve converter para fuso local ao exibir. Não editar status de equipamento diretamente pelo React ou Flutter.
7. **Testes:** testar requisições e respostas após instalar .NET SDK, EF CLI e SQL Server. Não considerar entrega aprovada antes dos testes reais.
8. **Escopo:** não foram criadas telas React/Flutter. Endpoints e JSON estão documentados em `CONTRATOS_API.md`, para Gustavo e Marcos consumirem.
