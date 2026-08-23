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

## O que vem depois

Estes **não** são tarefas acordadas — são os itens 2 a 5 da §9 do ARQUITETURA.md,
listados aqui só para a fila não terminar no F5. Vire cada um em `F` quando for a vez.

- **Relay outbox → RabbitMQ** (§4.4, item 2 da §9). A outbox já é escrita desde o F4;
  falta quem a leia. Pronto: eventos do dia chegando a uma fila de teste.
- **Operações + Custódia** (item 3). Primeiro consumidor real dos eventos e do `asof`.
- **Custódia: snapshots e recálculo** (item 4).
- **Corpactions** (item 5). Por último de propósito — é o tema mais sutil e tudo acima
  funciona sem ele.

---

## Pendências conhecidas, não agendadas

Levantadas durante F3–F5 e registradas na memória do projeto com o motivo e as
alternativas rejeitadas. Nenhuma bloqueia o que está feito.

- **Sem camada de autenticação.** O molde protege `/v1` com `ApiKeyMiddleware`; o Hub
  não portou. Hoje é mitigado por isolamento de rede (o Hub não publica porta e o
  nginx não tem rota para ele), mas qualquer container na `tesouro-net` chama os
  endpoints sem credencial e não há como auditar quem chamou. Vira relevante no
  primeiro endpoint de **escrita**.
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
