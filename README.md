# Hub de Preços

API que centraliza dados de mercado de múltiplas fontes (Tesouro Direto e, futuramente,
ações e cripto) para uso por outros serviços da plataforma, entre eles o serviço de
custódia. Ver [`plataforma-docs/ARQUITETURA.md`](../plataforma-docs/ARQUITETURA.md)
(§4) para o desenho completo.

> Solução .NET 8 em Clean Architecture (`Hub.Domain`, `Hub.Application`,
> `Hub.Infrastructure`, `Hub.API`), seguindo os padrões catalogados em
> [`PADROES.md`](PADROES.md) — herdados do repo `tesouro-direto`.

## Setup local

### Pré-requisitos

- **.NET 8 SDK** (target `net8.0`).
- **Docker** + plugin **Docker Compose** (usado para subir o `db`).

### 1. Configurar o `.env` e subir o banco

```bash
cp .env.example .env   # preencha HUB_APP_PASSWORD
docker compose up -d db
```

O compose falha o boot se `HUB_APP_PASSWORD` estiver vazio (`${VAR:?}`). O valor
preenchido é a senha da role de aplicação `hub`, provisionada pelo hook
`infra/postgres/initdb/01-provision-hub.sh` — ver [`infra/postgres/README.md`](infra/postgres/README.md).

### 2. Configurar a connection string (user-secrets)

`src/Hub.API/appsettings.json` só tem host/porta/database — **nunca** a credencial.
Configure-a via `dotnet user-secrets` com a MESMA senha usada no passo 1:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5433;Database=hub;Username=hub;Password=<mesma-senha-do-.env>" \
  --project src/Hub.API
```

Sem isso, o boot da API falha rápido com uma mensagem indicando exatamente esse
comando (ver `src/Hub.API/Extensions/ConnectionStringGuard.cs`) — em vez de um erro
de autenticação confuso do Npgsql.

### 3. Rodar a API

```bash
dotnet run --project src/Hub.API
```

Verifique a saúde:

```bash
curl -sf http://localhost:5000/health/ready && echo OK
```

## Rodar via Docker

```bash
docker compose up -d db          # banco
docker build -t hub-api .        # imagem da API
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Host=host.docker.internal;Port=5433;Database=hub;Username=hub;Password=<mesma-senha-do-.env>" \
  hub-api
```

Em container, a credencial vem da env var `ConnectionStrings__DefaultConnection`
(não de user-secrets, que é só para desenvolvimento local).

## Testes

```bash
dotnet build Hub.sln -v q --nologo
```

Ainda não há suíte de testes (projeto em esqueleto, sem endpoints de negócio).
