# Revisão obrigatória antes de finalizar implementações

Antes de finalizar qualquer implementação:

- Não versionar credenciais, tokens, connection strings, fallbacks sensíveis ou valores equivalentes no código e nos testes.
- Usar valores sintéticos gerados em runtime nos testes, inclusive testes negativos, sem literais que sejam detectados como segredos.
- Cumprir nomenclatura e newline final conforme `.editorconfig`.
- Revisar os arquivos rastreados, o diff e o escopo de alterações.
- Executar build completo sem erros ou warnings.
- Executar e confirmar os testes relevantes aprovados, sem banco ou rede quando essa for a exigência do projeto.
- Executar e ler o relatório oficial de qualidade, não apenas o resultado do workflow ou do build.
- Verificar o escopo de segredos utilizado pelo CI, incluindo histórico quando exigido.
- Descrever explicitamente todas as verificações não executadas e suas limitações.
- Não declarar conclusão enquanto houver findings bloqueantes pendentes.
