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

- [ ] **F6** — implemente o relay outbox → RabbitMQ (seção 4.4 do ARQUITETURA.md):
  compose com o broker, exchange `prices`, e o processo que lê `outbox` com
  `publicado_em IS NULL` ordenado por `id`, publica com publisher confirms na
  `routing_key` do registro e marca `publicado_em`. A garantia é at-least-once
  (ADR-3): pode duplicar entre o publish e o update, nunca perder. **Pronto:**
  eventos do dia chegando a uma fila de teste.

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

**Antes do F7, resolva duas dívidas** que só mordem quando existir consumidor de
verdade, ambas registradas na memória do projeto com motivo e alternativas:

1. O Hub só roda em **uma réplica** enquanto o Quartz usar store em memória —
   `[DisallowConcurrentExecution]` vale por processo.
2. A API key é **uma só para todos os consumidores**. Quando Custódia e Operações
   chegarem, as duas usam a mesma chave, sem como auditar quem chamou nem revogar
   uma sem revogar a outra. A tabela de client keys do molde está como ausência
   decidida — introduzi-la **depois** que houver integrador é breaking change.

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
