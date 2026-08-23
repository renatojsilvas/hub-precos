# Hub de Preços

API que vai centralizar dados de mercado de múltiplas fontes (Tesouro Direto e,
futuramente, ações e cripto) para uso por outros serviços da plataforma, entre eles
o serviço de custódia. Ver [`plataforma-docs/ARQUITETURA.md`](../plataforma-docs/ARQUITETURA.md)
(§4) para o desenho completo.

> Solução .NET 8 em Clean Architecture (`Hub.Domain`, `Hub.Application`,
> `Hub.Infrastructure`, `Hub.API`), seguindo os padrões catalogados em
> [`PADROES.md`](PADROES.md) — herdados do repo `tesouro-direto-api`.

## Estado atual

A API sobe, aplica migration no boot e responde health/metrics/swagger. O schema da
§4.1 do plano (`instrumentos`, `instrumento_fontes`, `precos`, `outbox`) existe, e o
**job de ingestão do Tesouro Direto** (§4.3) roda a cada 15 min: sonda condicional,
discovery aditivo do universo, backfill por janelas, delta D-5, e gravação da
canônica com evento na outbox na mesma transação.

O que **ainda não existe**: nenhum endpoint de negócio sob `/v1` (nem `GET
/prices/asof` nem o catálogo da §4.5), e o relay outbox → RabbitMQ da §4.4 — a outbox
é escrita desde já, mas ninguém a lê ainda. É o item 2 da ordem de implementação.

## Rodar com Docker (caminho padrão)

Sobe banco e API já conectados entre si, sem precisar de SDK .NET local.

```bash
cp .env.example .env   # preencha HUB_APP_PASSWORD e HUB_API_KEY
docker compose up -d
curl -sf http://127.0.0.1:5080/health/ready && echo OK
curl -sf -H "X-Api-Key: $(grep ^HUB_API_KEY= .env | cut -d= -f2)" http://127.0.0.1:5080/v1/instruments
```

O compose falha o boot se `HUB_APP_PASSWORD` ou `HUB_API_KEY` estiverem vazios
(`${VAR:?}`). São dois segredos com papéis diferentes:

- `HUB_APP_PASSWORD` é a senha da role de aplicação `hub`: o serviço `db` a usa para
  provisionar a role (hook `infra/postgres/initdb/01-provision-hub.sh`, ver
  [`infra/postgres/README.md`](infra/postgres/README.md)), e o serviço `app` a usa para
  montar `ConnectionStrings__DefaultConnection` (`Host=db;Port=5432`, a rede interna do
  compose) — a mesma senha nos dois lados, senão a autenticação no Postgres falha.
- `HUB_API_KEY` é a chave que o Hub **exige** de quem o chama: todo request a `/v1/*`
  precisa do header `X-Api-Key` com esse valor, ou recebe `401`
  (`src/Hub.API/Middleware/ApiKeyMiddleware.cs`). Ela chega ao container como
  `ApiKey__Key`. Precisa ter no mínimo 32 caracteres — `ApiKeyGuard` recusa o boot em
  `Production` com uma chave mais curta (ver seção Autenticação abaixo); gere uma com
  `openssl rand -hex 32`. `ApiKeyGuard` também recusa o boot se a chave contiver um
  placeholder conhecido (`CHANGE-ME-IN-PRODUCTION`, `dev-local-key`,
  `uma-chave-qualquer-para-dev`, com qualquer separador entre `-`, `_`, `.` ou
  espaço), mesmo que o comprimento mínimo seja atingido — comprimento e conteúdo são
  checagens independentes. **Não é a mesma chave que `TD_API_KEY`** (abaixo,
  opcional): aquela é a chave que o Hub **envia** para autenticar contra a TD API —
  direção oposta, sem relação uma com a outra.

Esse caminho não usa `dotnet user-secrets` em nenhum momento; as credenciais chegam só
por variável de ambiente.

- `app` publicado em `http://127.0.0.1:5080` — Swagger em `http://127.0.0.1:5080/swagger`.
- `db` publicado em `127.0.0.1:5433` — serve para `psql` e para o `dotnet run` local
  (abaixo), não para o `app` do compose (que fala com `db` pela rede interna, porta 5432).

## Rodar local para desenvolver (`dotnet run`)

Ciclo rápido de edição/depuração, sem rebuildar imagem a cada mudança. Precisa do
.NET 8 SDK instalado.

```bash
# 1. Só o banco, via compose
cp .env.example .env   # preencha HUB_APP_PASSWORD, se ainda não fez
docker compose up -d db

# 2. Credencial da API via user-secrets (mesma senha do passo 1)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5433;Database=hub;Username=hub;Password=<mesma-senha-do-.env>" \
  --project src/Hub.API

# 3. Rodar
dotnet run --project src/Hub.API
curl -sf http://localhost:5100/health/ready && echo OK
```

Swagger em `http://localhost:5100/swagger`.

`src/Hub.API/appsettings.json` só guarda host/porta/database — nunca a credencial
(`PADROES.md` §6) — por isso o passo 2 é obrigatório: sem ele o boot falha rápido com
uma mensagem indicando exatamente esse comando (`ConnectionStringGuard`, ver
`src/Hub.API/Extensions/ConnectionStringGuard.cs`), em vez de um erro de autenticação
confuso do Npgsql. **A armadilha**: `dotnet user-secrets` só é lido quando
`ASPNETCORE_ENVIRONMENT=Development`, e é `src/Hub.API/Properties/launchSettings.json`
quem define isso para o `dotnet run` — sem esse arquivo (ou rodando a API de outro
jeito, sem essa variável), o secret configurado no passo 2 é ignorado em silêncio e o
boot falha por falta de credencial mesmo com o secret salvo.

**`ApiKey:Key` não precisa de user-secrets em `Development`:** `ApiKeyGuard` (ver
`src/Hub.API/Extensions/ApiKeyGuard.cs`) recusa o boot fora de `Development`/`Testing`
por chave vazia, por conter um placeholder conhecido ou por ter menos de 32 caracteres
(ver seção Autenticação abaixo) — nenhuma dessas três checagens roda em
`Development`/`Testing` — em `dotnet run` local a API sobe mesmo sem configurar nada,
mas todo request a `/v1/*` continua exigindo o header `X-Api-Key`
(`ApiKeyMiddleware` roda em todo ambiente, guarda de boot ou não). Como
`appsettings.json` commita a chave vazia, qualquer requisição autenticada localmente
falha até você configurar uma — mais simples via user-secrets:

```bash
dotnet user-secrets set "ApiKey:Key" "uma-chave-qualquer-para-dev" --project src/Hub.API
curl -sf -H "X-Api-Key: uma-chave-qualquer-para-dev" http://localhost:5100/v1/instruments
```

`dotnet run` sobe em `http://localhost:5100` — porta fixa escolhida para não colidir
com o AirPlay Receiver do macOS (porta 5000) nem com o `app` do compose (porta 5080).

## Quando usar cada caminho

- **Docker (`docker compose up -d`)** — mais perto de produção (mesma imagem, mesma
  forma de receber credencial por env var); não precisa de SDK .NET instalado; toda
  mudança de código exige rebuild da imagem (`docker compose up -d --build`).
- **`dotnet run`** — ciclo rápido de edição/depuração local; exige SDK .NET e o passo
  de `user-secrets`; só o `db` roda em container.

## Estrutura da solução

| Projeto | Papel |
|---------|-------|
| `Hub.Domain` | Entidades, Value Objects e erros de domínio (`Result`/`Error`). Zero dependências externas. |
| `Hub.Application` | Casos de uso via MediatR (commands/queries) e interfaces de porta; `LoggingBehavior` no pipeline. |
| `Hub.Infrastructure` | EF Core (escrita/migrations) e, no futuro, Dapper (leitura), clients externos e jobs. |
| `Hub.API` | Minimal API — endpoints finos, middleware (correlation id), Swagger, health checks, métricas. |

## Padrões

Este repo segue os padrões catalogados em [`PADROES.md`](PADROES.md), herdados do
repo de referência [`../tesouro-direto-api`](../tesouro-direto-api) — antes de criar
qualquer estrutura nova (endpoint, repositório, job), localize o equivalente lá e
siga o molde.

## Autenticação

Todo request sob `/v1/*` exige o header `X-Api-Key` com o valor configurado em
`ApiKey:Key` (`ApiKey__Key` por variável de ambiente); sem ele, ou com valor errado,
a resposta é `401` em `application/problem+json`, indistinguível entre "sem chave" e
"chave errada" — não dá para descobrir por tentativa se uma chave existe.
`/health`, `/health/ready`, `/health/live`, `/metrics` e `/swagger` são isentos
(`ApiKey:ExcludedPaths`). A comparação é em tempo constante
(`CryptographicOperations.FixedTimeEquals` sobre SHA-256), para não vazar por
temporização se uma chave começa certa. Ver
`src/Hub.API/Middleware/ApiKeyMiddleware.cs`.

**Só existe a service key.** O molde de `tesouro-direto-api`
(`src/TesouroDireto.API/Middleware/ApiKeyMiddleware.cs`) também aceita uma *client
key* por usuário, resolvida numa tabela de API keys — o Hub não tem essa tabela e não
atende usuário final (`plataforma-docs/ARQUITETURA.md` §4.5: "o Hub não é exposto ao
front"), então só a chave única de serviço foi portada. Pelo mesmo motivo, o
rate limiter de falha de autenticação do molde (`RateLimiting:AuthFailure`) também não
foi portado: o Hub não tem rate limiting algum, não é exposto à internet (sem porta
publicada em produção, sem rota no nginx) — revisitar se isso mudar.

Fora de `Development`/`Testing`, o boot falha se `ApiKey:Key` estiver vazia, contiver
um placeholder conhecido (`CHANGE-ME-IN-PRODUCTION`, `dev-local-key`,
`uma-chave-qualquer-para-dev`, comparado ignorando separador — `-`, `_`, `.` ou
espaço — e maiúsculas/minúsculas) ou tiver menos de 32 caracteres
(`src/Hub.API/Extensions/ApiKeyGuard.cs`, mesmo padrão do `ConnectionStringGuard` para
a checagem de vazia/comprimento) — uma chave esquecida, curta demais (`123`, por
exemplo) ou deixada no placeholder de exemplo nunca vira "sem autenticação" em
silêncio. Gere uma chave forte com `openssl rand -hex 32`.

**Não confundir com `TdApi:ApiKey`:** essa é a chave que o Hub *envia* para autenticar
contra a TD API; `ApiKey:Key` é a chave que o Hub *exige* de quem o chama — direções
opostas, sem relação uma com a outra. Ver `.env.example`.

## GET /v1/prices/asof

Devolve, por instrumento, o preço vigente numa data (forward-fill por campo). Aceita
`instruments` (lista de ids separados por vírgula) ou, sem esse parâmetro, devolve o
catálogo inteiro.

**Decisão registrada (PADROES §2/§9):** a paginação (`page`/`pageSize`, default 100,
máx 500) é **sempre** aplicada, inclusive quando `instruments` é informado e `page`
não — diferente do molde de `tesouro-direto-api`, onde omitir `page` devolve a
coleção inteira. Aqui um cliente pode pedir centenas de instrumentos de uma vez
(`instruments=td:a,td:b,...`), então uma coleção sem teto violaria a proibição de
PADROES §9 a "resposta de coleção sem limite superior em contrato novo". Em ambos os
casos (com ou sem `instruments`), `X-Total-Count` reflete o total de instrumentos
distintos pedidos (ids duplicados, inclusive por diferença de maiúscula/minúscula,
contam uma vez só), não o tamanho da página devolvida — é assim que um cliente
percebe que a resposta foi cortada.

## Banco de dados

Ver [`infra/postgres/README.md`](infra/postgres/README.md) — provisionamento da role
`hub`, os dois caminhos de execução do SQL e a armadilha do volume já inicializado.

## Deploy

Automático: todo push na `main` que passe no job `test` dispara o job `deploy`
(`.github/workflows/ci.yml`), que entra na VPS por SSH, atualiza o clone em
`/opt/hub-precos`, provisiona o banco e sobe o container.

Topologia de produção, diferente da local em dois pontos:

- **Banco compartilhado.** Não há serviço `db` em produção. O Hub usa o Postgres que
  já roda na VPS, com role e database próprios, conforme a §12 do plano de
  arquitetura (`td_api`, `hub`, `custodia` e `operacoes` no mesmo cluster). Quem cria
  o database e converge a role é `infra/postgres/provision-remote.sh`, executado a
  cada deploy — é idempotente de propósito, então um cluster novo se conserta sozinho.
- **Sem porta publicada.** O Hub é interno entre serviços: não passa por nginx e não
  expõe porta no host. Quem precisar falar com ele o alcança por `hub-precos-app:8080`
  dentro da rede `tesouro-net`. Para depurar na VPS, use
  `docker exec hub-precos-app curl -sf http://localhost:8080/health/ready` — `curl` a
  partir do host não tem por onde chegar.

- **Uma réplica só, e isso não é negociável hoje.** O job de ingestão é agendado por
  Quartz com o store em memória (`RAMJobStore`), sem `UsePersistentStore` nem
  clustering. O `[DisallowConcurrentExecution]` que impede sobreposição vale **por
  processo**: com duas réplicas, cada uma tem seu próprio scheduler disparando o mesmo
  cron no mesmo instante, e uma não enxerga a outra. Duas execuções concorrentes que
  leiam as revisões correntes antes de qualquer uma commitar calculam a mesma
  `revisao + 1` e colidem na PK de `precos`. A colisão é tratada (o instrumento entra
  em `InstrumentosComFalha` e o ciclo segue), mas é trabalho jogado fora. Escalar
  horizontalmente exige antes migrar o Quartz para store persistente com clustering.
  Nenhum teste captura essa restrição — ela vive aqui.

## Ingestão do Tesouro Direto

O ciclo roda sozinho a cada 15 min (`TdApi:CronSchedule`). Chaves de
`appsettings.json` que valem conhecer:

| Chave | Default | Para que serve |
|---|---|---|
| `TdApi:CronSchedule` | `0 0/15 * * * ?` | Cadência do ciclo |
| `TdApi:AgendamentoAtivo` | `true` | `false` desliga o agendamento (usado no ambiente de teste) |
| `TdApi:JanelaDeltaDias` | `5` | Quantos dias para trás o delta relê, de propósito, para captar correção |
| `TdApi:JanelaBackfillAnos` | `2` | Tamanho da janela de datas no backfill |
| `TdApi:PisoPrograma` | `2002-01-07` | Início do programa; serve de sanity check da âncora |
| `TdApi:TamanhoLote` | `1000` | Linhas por transação |

Todo valor inválido cai no default **com aviso no log**, nunca em silêncio.

**Invariante de `observado_em`:** quem escrever em `precos` deve gravar `observado_em`
com o instante corrente do `TimeProvider` injetado, nunca uma data derivada do dado
(ex.: a `dataBase` do preço). O `ETag` de `GET /v1/prices/asof`
(`ContentVersionProvider`) é calculado a partir de `MAX(observado_em)` por razão de
performance (`precos` chega a milhões de linhas; `COUNT(*)` a cada request seria seq
scan) — isso só é seguro porque `observado_em` é monotônico com o tempo de ingestão.
Um writer que grave um timestamp histórico em `observado_em` quebra essa invariante
em silêncio: o token do ETag não muda, e um cliente com `If-None-Match` recebe `304`
mesmo com dado novo no banco.

Botão de reparo: apagar os preços de um instrumento força o re-backfill dele no ciclo
seguinte, porque o watermark é derivado de `MAX(data_ref)` e não existe como coluna
(ADR-5). Não há estado de progresso para limpar junto.

**Linha `EodPricesReady` da outbox não é podável.** Quando existir poda de outbox, ela
precisa excluir `tipo = 'EodPricesReady'`. A dedupe do EOD deriva de ler a própria
outbox — a linha **é** o fato "já anunciei o dia D" —, então apagá-la faz o dia ser
reemitido. São ~365 linhas por ano; o custo de guardar é irrelevante perto do de
descobrir isso depois. Não há poda implementada hoje: isto é decisão registrada em
antecedência, não conserto.

Segredos necessários em `Settings → Secrets and variables → Actions`: `VPS_HOST`,
`VPS_USER`, `VPS_SSH_KEY` (chave privada dedicada ao deploy, não a pessoal),
`HUB_APP_PASSWORD` (senha da role `hub` no cluster compartilhado — distinta da do
`.env` local, que é do container de desenvolvimento), `HUB_API_KEY` (a chave que o
Hub em produção exige de quem o chama via `X-Api-Key` — também distinta do valor do
`.env` local; sem ela o job de deploy escreve `HUB_API_KEY` vazio no `.env` remoto, o
que o próprio job detecta e recusa antes de subir o container, e mesmo que chegasse a
subir o `ApiKeyGuard` derrubaria o boot em `Production`) e `TD_API_KEY` (a chave que o
Hub em produção **envia** à TD API via `X-Api-Key` — direção oposta à `HUB_API_KEY`,
sem relação com ela; sem esse secret o job de deploy recusa escrever o `.env` remoto e
falha antes de subir o container, porque sem essa chave o job de ingestão dispara a
cada 15 min e toda chamada à TD API recebe 401 — o Hub sobe healthy e serve listas
vazias, sem erro visível de fora).

O job falha cedo se a rede `tesouro-net` ou o container `tesouro-direto-db` não
existirem, antes de construir imagem ou tocar em qualquer container.

**Sem rollback automático:** se a versão nova subir e não ficar `healthy`, o job falha
e o container fica no ar quebrado — não volta sozinho para a anterior. Aceitável
enquanto não há consumidor; resolver antes de existir.
