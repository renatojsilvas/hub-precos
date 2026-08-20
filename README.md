# Hub de Preços

API que vai centralizar dados de mercado de múltiplas fontes (Tesouro Direto e,
futuramente, ações e cripto) para uso por outros serviços da plataforma, entre eles
o serviço de custódia. Ver [`plataforma-docs/ARQUITETURA.md`](../plataforma-docs/ARQUITETURA.md)
(§4) para o desenho completo.

> Solução .NET 8 em Clean Architecture (`Hub.Domain`, `Hub.Application`,
> `Hub.Infrastructure`, `Hub.API`), seguindo os padrões catalogados em
> [`PADROES.md`](PADROES.md) — herdados do repo `tesouro-direto-api`.

## Estado atual

Esqueleto de solução: os 4 projetos existem, compilam, a API sobe, aplica migration
no boot e responde health/metrics/swagger — mas **não há nenhum endpoint de negócio**
(nada sob `/v1`, nenhuma entidade de domínio) e **não há suíte de testes**. Se você
veio procurar preços/instrumentos, ainda não existe; o que existe é a base para
construir isso seguindo os padrões do repo de referência.

## Rodar com Docker (caminho padrão)

Sobe banco e API já conectados entre si, sem precisar de SDK .NET local.

```bash
cp .env.example .env   # preencha HUB_APP_PASSWORD
docker compose up -d
curl -sf http://127.0.0.1:5080/health/ready && echo OK
```

O compose falha o boot se `HUB_APP_PASSWORD` estiver vazio (`${VAR:?}`). Esse valor é
a senha da role de aplicação `hub`: o serviço `db` a usa para provisionar a role
(hook `infra/postgres/initdb/01-provision-hub.sh`, ver
[`infra/postgres/README.md`](infra/postgres/README.md)), e o serviço `app` a usa para
montar `ConnectionStrings__DefaultConnection` (`Host=db;Port=5432`, a rede interna do
compose) — a mesma senha nos dois lados, senão a autenticação falha. Esse caminho não
usa `dotnet user-secrets` em nenhum momento; a credencial chega só por variável de
ambiente.

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

Segredos necessários em `Settings → Secrets and variables → Actions`: `VPS_HOST`,
`VPS_USER`, `VPS_SSH_KEY` (chave privada dedicada ao deploy, não a pessoal) e
`HUB_APP_PASSWORD` (senha da role `hub` no cluster compartilhado — distinta da do
`.env` local, que é do container de desenvolvimento).

O job falha cedo se a rede `tesouro-net` ou o container `tesouro-direto-db` não
existirem, antes de construir imagem ou tocar em qualquer container.

**Sem rollback automático:** se a versão nova subir e não ficar `healthy`, o job falha
e o container fica no ar quebrado — não volta sozinho para a anterior. Aceitável
enquanto não há consumidor; resolver antes de existir.
