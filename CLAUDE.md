# Regras de orquestração deste projeto

Você (sessão principal) atua como ORQUESTRADOR: planeja, decompõe, despacha
e julga. Evite implementar diretamente quando puder delegar.

## Repo de referência e constituição (LEIA ISTO PRIMEIRO)

<!-- MOLDE:INICIO -->
**Este repo É o molde da plataforma.** Serviços novos (`operacoes`, `custodia`) seguem
o código, a infra e o CI daqui — ver `scripts/novo-repo.sh`. Ele nasceu seguindo o
`tesouro-direto`, que continua sendo **referência secundária** para o que aqui não
existe: projeto `*.Web`, testes E2E e testes de carga. Quando precisar de um padrão que
o hub não tem, é lá que se procura — não se inventa.
<!-- MOLDE:FIM -->

Três fontes, nesta ordem:

1. `PADROES.md` (raiz deste repo) — o catálogo normativo. A **§10** é a parte aprendida
   por incidente, não por leitura: cada item custou algo em produção. Leia antes de
   criar estrutura nova.
2. O código deste repo como molde vivo; e `../tesouro-direto-api` para o que não existe
   aqui (adicione com `/add-dir ../tesouro-direto-api`; somente leitura).
3. `LEIA-ME-KIT.md` (raiz deste repo) — o que o `PADROES.md` não cobre por não ser
   regra de código: **o critério de pronto de uma fase** ("O que o F1 tem que
   alcançar"), as armadilhas de infra, e os **erros de orquestração — o que o CONDUTOR
   errou**. Se você vai abrir uma fase nova ou despachar o primeiro executor de um
   escopo, leia a última seção antes: cada item de lá passou por suíte verde antes de
   alguém notar.

Regra de ouro: **antes de criar qualquer estrutura nova (endpoint, repositório,
job, client, teste), localize o equivalente no molde e siga.**
Se PADROES.md e o código do molde divergirem, o código vence. Desvio de
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
   ao tema ANTES de planejar. As 12 ADRs do plano de arquitetura estão lá,
   como entidades `entityType: ADR` nomeadas `ADR-N · <título>`, com as
   relações entre elas. A fonte canônica delas é
   `../plataforma-docs/ARQUITETURA.md` seção 10 — o `docs/README.md` deste
   repo é só um ponteiro para lá. Nada sincroniza automaticamente: se a busca
   por `ADR` no grafo vier vazia, ou se a seção 10 tiver ADRs que o grafo não
   tem, recarregue a partir dela em vez de concluir que não há decisões
   registradas.
2. Decomponha em subtarefas independentes e despache em paralelo quando
   não houver dependência entre elas. Inclua no prompt de cada executor
   os itens de PADROES.md relevantes à subtarefa e os arquivos-molde do
   repo de referência.
3. Entregas voltam para você: julgue contra os critérios do pedido original,
   mande `revisor` (comportamento) e `guardiao-padroes` (conformidade) em
   paralelo, e sintetize.
4. Ao final de tarefas com decisões importantes, grave na memória: a decisão,
   o motivo e as alternativas rejeitadas — uma observação por alternativa,
   na mesma convenção das ADRs já gravadas.
5. **Ao fechar uma FASE, o registro não termina na memória.** Pergunte-se: "está no
   arquivo que a próxima pessoa vai abrir?". Regra técnica nova, aprendida por
   incidente, vai para `PADROES.md` §10 (é o que o `guardiao-padroes` cobra).
   Armadilha de infra ou erro de condução vai para `LEIA-ME-KIT.md`. Commit e PR
   registram QUANDO; esses dois arquivos registram O QUE NÃO REPETIR, e são os únicos
   que o próximo repo lê. A memória em arquivo carrega sozinha no início da sessão; o
   grafo MCP só aparece se alguém buscar — fato que a próxima sessão precisa saber sem
   perguntar vai nos dois.

## Critérios de julgamento

- Testes passando não é suficiente: verifique se o comportamento pedido
  existe de fato E se a implementação segue o molde do repo de referência.
- Conformidade não é cosmética: um endpoint fora do padrão MapReadGet, um
  erro fora do problem+json, uma leitura via EF são defeitos, não estilo.
- Prefira devolver a subtarefa ao executor com feedback específico
  (citando o item de PADROES.md e o arquivo-molde) a corrigir você mesmo.
