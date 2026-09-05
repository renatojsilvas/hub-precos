# ROADMAP do Hub de Preços

Fila de tarefas do Hub, uma por vez. **Como usar:** abra este arquivo, copie o texto
do próximo `F` não marcado, cole na sessão, aceite a entrega, marque o checkbox e
commite. O arquivo é a fonte — não o que estiver no contexto de alguma sessão.

A fonte canônica de arquitetura é `../plataforma-docs/ARQUITETURA.md`; o repo de
referência dos padrões é `../tesouro-direto-api` (ver `PADROES.md` e `CLAUDE.md`).

---

## Fila

- [x] **F1** — ultracode: crie o esqueleto da solução (Hub.API, Hub.Application,
  Hub.Domain, Hub.Infrastructure) seguindo o molde de `../tesouro-direto-api` —
  Directory.Build.props, Dockerfile multi-stage, Serilog+CorrelationId,
  health/metrics, migrations no boot conectando como role `hub` no Postgres da porta
  5433. Sem endpoints de negócio.

- [x] **F2** — implemente o schema da seção 4.1 de `../plataforma-docs/ARQUITETURA.md`
  (instrumentos, instrumento_fontes, precos, outbox) como migrations EF, com os
  índices especificados, snake_case.
  <br>→ PR #8.

- [x] **F3** — implemente a interface `IPriceSourceAdapter` (seção 4.2 do
  ARQUITETURA.md) e o `TDApiAdapter`: typed client com Polly para a TD API
  (X-Api-Key por config), discovery (seção 4.3 passo 2) com upsert aditivo em
  instrumentos.
  <br>→ PR #9.

- [x] **F4** — implemente o job de ingestão unificado (seção 4.3 completa do
  ARQUITETURA.md): sonda ETag, watermarks derivados por MAX (ADR-5), backfill por
  janelas de datas com sonda de âncora (ADR-7), delta D-5, upsert com detecção de
  mudança gravando outbox na mesma transação (ADR-3).
  <br>→ PR #10.

- [x] **F5** — implemente `GET /prices/asof` e `GET /instruments` (seção 4.5 do
  ARQUITETURA.md) no padrão MapReadGet do repo de referência, com testes de contrato
  completos (status, headers, problem+json com codes).
  <br>→ PR #11.

Com o F5, fecha o **item 1 da ordem de implementação** (§9 do ARQUITETURA.md), cujo
critério de pronto é "fluxo 0 executado; ciclo diário populando a canônica; `asof`
respondendo".

---

## Próximos passos

Redigidos no mesmo formato dos anteriores — copie o texto e cole na sessão. Seguem a
ordem da §9 do ARQUITETURA.md, que é deliberada: cada etapa é utilizável sozinha e não
exige retrabalho na seguinte.

- [x] **F6** — implemente o relay outbox → RabbitMQ (seção 4.4 do ARQUITETURA.md):
  compose com o broker, exchange `prices`, e o processo que lê `outbox` com
  `publicado_em IS NULL` ordenado por `id`, publica com publisher confirms na
  `routing_key` do registro e marca `publicado_em`. A garantia é at-least-once
  (ADR-3): pode duplicar entre o publish e o update, nunca perder. **Pronto:**
  eventos do dia chegando a uma fila de teste.
  <br>→ PR #22 (relay), #23 (deploy/CI), #24 (TTL do ETag), #25 (teste instável),
  #26 (recursos do broker) — e `tesouro-direto#78`, a outra metade da realocação
  de memória.

Com o F6, fecha o **item 2 da ordem de implementação** (§9 do ARQUITETURA.md), cujo
critério de pronto é "eventos do dia chegando a uma fila de teste". Verificado
localmente com fila `teste.relay` bindada em `eod.ready`; em produção o `eod.ready`
do fechamento ganhou `publicado_em` 4s depois do ciclo. Não há fila bindada em
produção — a mensagem vai ao exchange e é descartada, que é o comportamento certo
enquanto não existir consumidor (ADR-1: a canônica no Postgres é o arquivo).

- [ ] **F7** — Operações mínimo (`POST /operacoes` + outbox + `trades.registered`) e,
  na Custódia, fila com handlers de `TradeRegistered` e `PriceObserved`, livro, posição
  corrente e bootstrap da projeção via REST (seção 9, item 3). **Pronto:** aplicação
  registrada via Operações aparecendo no livro por evento, e preço do dia chegando por
  push.

- [ ] **F8** — Custódia: handler de `eod.ready`, worker da seção 7.4 (gatilhos 1 e 2) e
  extratos (item 4). **Pronto:** fluxo 2 retroativo e fluxo 5 funcionando ponta a ponta.

- [ ] **F9** — corpactions: gerador determinístico no Hub (cupom e vencimento) e
  tradução em movimentos com dedupe (item 5). Por último de propósito — é o tema mais
  sutil e tudo acima funciona sem ele. Comece o enum com o que as carteiras reais têm
  (compra, venda, cupom, resgate), não com a taxonomia completa da B3 no dia 1.

**Duas dívidas adiadas por decisão do dono (2026-09-05)**, ambas registradas na
memória do projeto com motivo e alternativas. Elas só mordem quando existir consumidor
de verdade, e a decisão foi que **não mordem nesta topologia** — mas cada uma tem
gatilho de revisita explícito, porque desvio sem gatilho vira dogma:

1. **Réplica única.** O Hub só roda em uma réplica enquanto o Quartz usar store em
   memória — `[DisallowConcurrentExecution]` vale por processo. O F6 acrescentou um
   segundo motivo com a mesma cura: o `SELECT` de pendentes da outbox não usa
   `FOR UPDATE SKIP LOCKED`, então duas instâncias leriam o mesmo lote e publicariam em
   dobro (inócuo para o consumidor sob at-least-once, que deduplica por chave natural,
   mas infla `hub_relay_eventos_publicados_total`).
   **Decisão:** não haverá duas réplicas do Hub. **Gatilho de revisita:** no dia em que
   alguém escalar horizontalmente, os DOIS pontos têm que ser resolvidos juntos — store
   persistente com clustering no Quartz **e** `FOR UPDATE SKIP LOCKED` na leitura da
   outbox. Escalar resolvendo só um deles publica evento em dobro em silêncio.

2. **API key única para todos os consumidores.** Custódia e Operações usarão a mesma
   chave, sem como auditar quem chamou nem revogar uma sem revogar a outra.
   **Decisão:** o acesso é de dentro da mesma VPS, pela rede docker interna — o Hub não
   fica exposto, e a chave única não amplia superfície. **Gatilho de revisita:** se
   algum consumidor passar a chamar de fora da rede docker, ou se for preciso saber
   **qual** serviço fez uma chamada (investigação de incidente, rate limit por
   consumidor, revogação seletiva). Note que introduzir a tabela de client keys
   **depois** que houver integrador é breaking change — o custo cresce com o tempo,
   então a revisita não deve esperar o problema doer.

---

## Pendências conhecidas, não agendadas

Levantadas durante F3–F5 e registradas na memória do projeto com o motivo e as
alternativas rejeitadas. Nenhuma bloqueia o que está feito.

- ~~**Sem camada de autenticação.**~~ Resolvido: `ApiKeyMiddleware` (service key só,
  sem tabela de client keys — o Hub não atende usuário final) protege `/v1/*` com
  `X-Api-Key`, guarda de boot recusa subir com a chave vazia fora de
  `Development`/`Testing`. Ver README.md "Autenticação". O rate limiter de falha de
  autenticação do molde continua fora — ausência decidida, revisitar se o Hub deixar
  de ser interno à rede docker.
- **Réplica única obrigatória.** O Quartz usa store em memória; `[DisallowConcurrentExecution]`
  vale por processo. Escalar horizontalmente exige antes store persistente com
  clustering. Documentado no `README.md`.
- **`Preco.Create` rejeita valor `<= 0`.** O adapter emite `0` como valor real (a fonte
  distingue `0` de ausente), e o upsert o descarta contando em `PrecosRejeitados`.
  Decidir entre tratar zero como caso especial ou revisar a validação do domínio.
- **A TD API descarta correção retroativa** (o importador de lá ignora `data_base` que
  já existe), então `revisao > 0` não tem como ser disparado pela fonte `td-api` hoje
  e o Fluxo 5 não é verificável ponta a ponta. Candidato a item da §13.
- **Sem índice para o filtro textual de `/v1/instruments`.** `ILIKE '%...%'` sobre ~400
  linhas; revisitar com `pg_trgm` quando a fase 2 abrir o universo.

Levantadas durante o F6:

- **Capacidade do Postgres compartilhado.** `precos` tem 288 MB (867 mil linhas) e o
  database `hub` chegou a 298 MB, contra 44 MB do `tesouro_direto`, no mesmo container
  — que precisou subir de 128 MB para 256 MB (`tesouro-direto#78`) depois de disparar
  alerta de reclaim sustentado. 256 MB cobre o working set, não o dataset. A ADR-12
  separa bancos por serviço mas **não separa orçamento de memória**: a instância é uma
  só. A fase 2 (ações e cripto) é onde se decide entre subir de novo ou dar instância
  própria ao Hub.
- **O exchange só nasce no primeiro publish.** A conexão é lazy e o handler não chama o
  publisher com a outbox vazia, então num ambiente novo o exchange `prices` não existe
  até o Hub publicar algo. O consumidor deve declarar o próprio exchange (declaração
  AMQP é idempotente com parâmetros idênticos) antes de fila e bindings, em vez de
  depender da ordem de subida. Documentado no README.
- **A sonda não distingue "tudo ingerido" de "nada existe".**
  `ExisteInstrumentoSemPrecoAsync` devolve `false` nos dois casos, e a mensagem de log
  diz "nenhum instrumento pendente de backfill" mesmo com o universo vazio — descreve
  um sistema em dia quando ele está zerado. O TTL do ETag (PR #24) removeu o efeito
  travante, mas o diagnóstico enganoso continua. Custou horas num incidente real.
- **`outbox.criado_em` sem `DEFAULT now()`** no schema gerado pelo EF, embora a DDL da
  §4.1 documente esse default. Inócuo pela aplicação (o EF sempre preenche); `INSERT`
  manual falha por `NOT NULL`.
