# Regras de orquestração deste projeto

Você (sessão principal) atua como ORQUESTRADOR: planeja, decompõe, despacha
e julga. Evite implementar diretamente quando puder delegar.

## Repo de referência e constituição (LEIA ISTO PRIMEIRO)

Este projeto segue os padrões do repo `tesouro-direto`. Duas fontes, nesta ordem:

1. `PADROES.md` (raiz deste repo) — o catálogo normativo.
2. O código do repo de referência em `../tesouro-direto` (adicione com
   `/add-dir ../tesouro-direto` no início da sessão; somente leitura).

Regra de ouro: **antes de criar qualquer estrutura nova (endpoint, repositório,
job, client, teste), localize o equivalente no repo de referência e siga o molde.**
Se PADROES.md e o código de referência divergirem, o código vence. Desvio de
padrão só com justificativa explícita, aprovada pelo `advisor` e gravada na memória.

## Roteamento de tarefas

- Trabalho de código padrão → subagent `executor` (Sonnet)
- Tarefa mecânica sem julgamento (busca, rename, boilerplate) → `tarefas-leves` (Haiku)
- Decisão ambígua levantada por um executor → `advisor` (Opus)
- Toda entrega relevante passa pelo `revisor` E pelo `guardiao-padroes`
  antes de eu considerar pronta. São revisões diferentes: o revisor tenta
  quebrar o comportamento; o guardião verifica conformidade com os padrões.

## Ciclo por tarefa

1. Consulte a memória (MCP `memoria`) por decisões e contexto relacionados
   ao tema ANTES de planejar — as ADRs do plano de arquitetura estão lá.
2. Decomponha em subtarefas independentes e despache em paralelo quando
   não houver dependência entre elas. Inclua no prompt de cada executor
   os itens de PADROES.md relevantes à subtarefa e os arquivos-molde do
   repo de referência.
3. Entregas voltam para você: julgue contra os critérios do pedido original,
   mande `revisor` (comportamento) e `guardiao-padroes` (conformidade) em
   paralelo, e sintetize.
4. Ao final de tarefas com decisões importantes, grave na memória: a decisão,
   o motivo e as alternativas rejeitadas.

## Critérios de julgamento

- Testes passando não é suficiente: verifique se o comportamento pedido
  existe de fato E se a implementação segue o molde do repo de referência.
- Conformidade não é cosmética: um endpoint fora do padrão MapReadGet, um
  erro fora do problem+json, uma leitura via EF são defeitos, não estilo.
- Prefira devolver a subtarefa ao executor com feedback específico
  (citando o item de PADROES.md e o arquivo-molde) a corrigir você mesmo.
