# PADROES.md — Constituição técnica (herdada do repo tesouro-direto-api)

Este arquivo é a referência normativa dos projetos novos (hub, operacoes, custodia).
Todo padrão aqui listado tem implementação de referência no repo `tesouro-direto-api`
(caminho em cada item). **Na dúvida entre este resumo e o código de referência, o
código de referência vence** — leia-o antes de decidir diferente, e desvios exigem
justificativa registrada na memória.

## 1. Arquitetura e camadas

- Solução em 4 projetos: `*.API` (endpoints finos), `*.Application` (commands/queries
  + handlers via MediatR/ISender), `*.Domain` (entidades, VOs, erros), `*.Infrastructure`
  (EF Core p/ escrita, Dapper p/ leitura, clients externos, jobs).
  Ref: `src/TesouroDireto.*/`
- Endpoint NUNCA contém lógica: recebe request → `ISender.Send` → `Result` →
  `ToHttpResult`. Ref: `src/TesouroDireto.API/Endpoints/*.cs`
- Erros de domínio como `Error` tipado com `ErrorType` (Validation|NotFound|Conflict),
  mapeados centralmente para HTTP (400/404/409) — nunca por string, nunca try/catch
  de fluxo. Ref: `Domain/Common/DomainErrors.cs`, `API/Extensions/ResultExtensions.cs`

## 2. Contratos HTTP

- Rotas de negócio versionadas sob grupo `/v1`; fora dele só health/metrics/swagger.
- Corpo de erro: `application/problem+json` com `detail`, extensão `code`
  (`recurso.motivo`), `correlationId`, `traceId`.
- Leituras via helper padrão (`MapReadGet`): GET+HEAD+OPTIONS, 405 com `Allow`,
  ETag/304 (`ConditionalGetFilter` sobre token global de versão), supressão de corpo
  em HEAD, `Cache-Control: public, max-age=300` só em 2xx.
  Ref: `API/Http/ReadEndpointExtensions.cs`
- Escritas: sem ETag; respostas sensíveis a tempo usam `no-store`.
- Paginação: `page/pageSize` com clamp (default 100, máx 500), `X-Total-Count` sempre,
  header `Link` (next/prev) só quando `page` informado. Coleções limitadas por
  construção podem dispensar paginação — decisão registrada.
- Identificadores públicos: **slugs determinísticos**, nunca uuid em contrato.
  Ref: racional do `codigo` de títulos.
- POST cria → 201 + `Location` via rota nomeada (`CreatedAtRoute`); PUT idempotente → 204.

## 3. Persistência

- CQRS leve: EF Core só na escrita; leituras em Dapper em ReadRepositories com SQL
  explícito. Ref: `Infrastructure/Persistence/Repositories/*ReadRepository.cs`
- Migrations EF aplicadas no boot + seed idempotente.
- Snake_case no banco; índices nomeados (`ix_tabela_colunas`); todo padrão de acesso
  novo exige índice correspondente na mesma migration.
- Upserts idempotentes por chave natural (`ON CONFLICT`); jobs re-executáveis sem
  efeito duplicado.
- **Banco privado por serviço**: role própria não-superuser, schema próprio com
  `REVOKE ... FROM PUBLIC`, UMA connection string por serviço, integração entre
  serviços SOMENTE por contrato (HTTP/eventos), jamais lendo banco alheio.

## 4. Integrações externas e jobs

- Cliente HTTP externo = typed client + Polly (retry exponencial + circuit breaker).
- Agendamento via Quartz; um job por responsabilidade; horários em config.
- Cache de dado externo com fallback explícito e não-silencioso
  (fresh + last-known-good com origem exposta no contrato).
  Ref: `CachedProjecaoMercadoService` (padrão Bcb|CacheFallback).
- Consumo de API própria/externa com GET condicional (If-None-Match) quando o
  provedor expõe ETag — sondar barato antes de coletar caro.

## 5. Mensageria (padrão novo, consolidado nas ADRs do plano)

- Transactional outbox obrigatória: efeito + evento na MESMA transação; relay
  publica at-least-once; consumidor deduplica por chave natural (`ref_externa` UNIQUE).
- Contratos de evento JSON com envelope `{"v": n, "tipo": ...}`; valores monetários
  como string decimal; campos novos sempre opcionais.
- Fila durável por serviço consumidor; manual ack pós-persistência.

## 6. Segurança e limites

- Auth por API key em header, middleware global, paths isentos explícitos.
- Rate limiting por config (`appsettings`), nunca hardcoded.
- Segredos por variável de ambiente/secrets; nunca em código ou compose commitado.

## 7. Observabilidade e operação

- Serilog estruturado + Correlation ID em toda requisição; logs → Loki/Grafana;
  métricas Prometheus em `/metrics`; health em `/health`, `/health/ready`, `/health/live`.
- Docker multi-stage; docker-compose com app + Postgres + observabilidade;
  CI GitHub Actions: testes → Sonar → deploy SSH. Ref: `Dockerfile`, `docker-compose.yml`,
  `.github/workflows/deploy.yml`
- Toda anomalia rara e significativa (ex.: revisão de dado retroativo) gera log
  destacado — raridade merece visibilidade.

## 8. Qualidade

- Testes: unidade (domínio/handlers), integração (repositórios/HTTP com banco real),
  e2e via compose (`run-e2e.sh` como modelo). Comportamento HTTP completo é testado:
  status, headers (X-Total-Count, Allow, ETag/304), corpo problem+json com codes.
- SonarQube local e no CI.
- Documentação de decisão: ADR curto por decisão estrutural (decisão, motivo,
  alternativas rejeitadas) — e gravado na memória MCP.

## 9. Antipadrões proibidos (com a razão)

- Integração via banco de outro serviço (contorna contrato; migration alheia vira
  breaking change silencioso).
- Endpoint com lógica de negócio; erro mapeado por string; exception como fluxo.
- Resposta de coleção sem limite superior em contrato novo.
- Estado de controle duplicando dados (flags de bootstrap) — derive dos dados
  (padrão watermark/reconciliação).
- Publicar evento fora da transação do efeito (dois commits que divergem).
- Forward-fill materializado como observação; edição destrutiva de fato — corrija
  por revisão/estorno preservando a versão anterior.
