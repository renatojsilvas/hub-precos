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
- **Toda porta de repositório devolve `Result<T>` — leitura inclusive.** No repo de
  referência isso é uniforme: `ITituloReadRepository` e `IPrecoTaxaReadRepository` têm
  seis métodos entre os dois, todos `Task<Result<...>>`. Porta de leitura com tipo cru
  não tem como reportar falha sem exceção, e força o chamador a escolher entre engolir
  o erro ou usar try/catch de fluxo — os dois proibidos pela §9. A implementação vive
  na Infrastructure, e é lá que exceção de infraestrutura vira `Result`; a Application
  nunca captura. Ref: `src/TesouroDireto.Application/{Titulos,PrecosTaxas}/I*ReadRepository.cs`
- Parâmetro de porta usa o **value object** onde ele existe (identidade e classificação),
  e primitivo só onde não há VO — data usada como filtro ou relógio continua `DateOnly`,
  termo de busca livre continua `string`. Ref: `ITituloWriteRepository.ExistsAsync(TipoTitulo,
  DataVencimento, ct)` vs. `IPrecoTaxaReadRepository.GetByTituloIdAsync(Guid, DateOnly?, DateOnly?, ct)`
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
- Guarda de boot por credencial obrigatória: fora dos ambientes isentos, segredo
  ausente **derruba o boot** — nunca significa "sem verificação". Guardas irmãs podem
  isentar ambientes diferentes, e a resposta depende do que falta sem a credencial:
  sem connection string a aplicação não sobe de jeito nenhum (isenta só `Testing`);
  sem API key ela sobe e o middleware fica fail-closed nas rotas de negócio, o que é
  útil localmente (isenta `Testing` e `Development`). Diferença deliberada — se
  divergir, registre o porquê. A guarda de API key também rejeita chave abaixo de um
  comprimento mínimo (32 caracteres), não só vazia — uma chave curta sobe sem
  reclamação e não é menos perigosa que uma vazia — e rejeita placeholders conhecidos
  (`CHANGE-ME-IN-PRODUCTION`, `dev-local-key`, e outros que apareçam na própria
  documentação do repo), mesmo quando o valor configurado atinge o comprimento
  mínimo: comprimento e conteúdo são camadas independentes, uma não substitui a
  outra. Ref: `API/Extensions/{ConnectionStringGuard,ApiKeyGuard}.cs`

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

## 10. Lacunas do molde (aprendidas por incidente, não por leitura)

Os itens de 1 a 9 vêm do repo de referência. **Os desta seção não** — cada um nasceu
de algo que quebrou de verdade ao construir o `hub`, porque o molde não tinha o caso
ou porque o porte perdeu uma peça. O molde continua sendo a fonte: o que esta seção
diz é onde ele **não basta**.

Se você está criando `operacoes` ou `custodia`, leia isto **antes** de portar — foi
escrito para você não repetir.

### 10.1. Nome de serviço tem que ser único NA REDE, não só no arquivo

Ao entrar numa rede compartilhada (`external: true`), confira os aliases já
registrados antes de escolher o nome do serviço no compose.

**Por quê:** o Compose registra um alias DNS igual ao **nome do serviço**;
`container_name` acrescenta um nome, não substitui o alias. Um serviço `app` entrando
numa rede que já tem outro `app` faz o DNS do Docker guardar os dois IPs sob o mesmo
nome e devolver a lista alternada — sem erro, sem log. Metade das chamadas vai para o
vizinho. Aconteceu: o front do `tesouro-direto` passou a receber 404, e metade das
raspagens de métrica dele vinha do Hub, poluindo a série de onde saem 12 das 18
regras de alerta. Ocupados hoje na `tesouro-net`: `app`, `db`, `web`, `alloy`,
`tesouro-direto-*`.

**Guarda:** o job de deploy compara os nomes de serviço do compose com os aliases de
terceiros na rede e aborta antes do `up`. Cada repo precisa da sua — a do Hub só
protege o Hub. Ver `.github/workflows/ci.yml`.

### 10.2. Provisionar credencial sem verificá-la é afirmar sem evidência

Todo script que cria ou converge uma credencial tem que **testar** essa credencial
antes de reportar sucesso, e falhar se ela não autenticar.

**Por quê:** o molde provisiona a role e termina. Quando o valor divergiu, o
provisionamento passou verde e o defeito apareceu minutos depois, como crash-loop da
aplicação num container à parte — longe da causa e caro de diagnosticar. Com a
verificação, o deploy morre no passo certo, com a mensagem certa.

### 10.3. Verificação de credencial passa pela rede, nunca por loopback

Use um container cliente à parte na mesma rede docker. **Nunca**
`docker exec ... psql -h 127.0.0.1`.

**Por quê:** o `pg_hba.conf` da imagem oficial do Postgres tem
`host all all 127.0.0.1/32 trust`. Por loopback, **qualquer senha autentica** — a
verificação passaria sempre e seria teatro. Medido nos dois sentidos: pela rede
rejeita senha errada, por loopback aceita.

### 10.4. Segredo que alimenta dois caminhos tem que ser normalizado na origem

Se o mesmo segredo chega ao provisionamento por um caminho e à aplicação por outro,
normalize (`tr -d '\r\n'`) **uma vez, antes de qualquer uso**.

**Por quê:** `docker exec -e` preserva o valor byte a byte; o `.env` do compose é lido
**por linha** e descarta um `\n`/`\r` final. Um segredo colado com quebra de linha faz
a role nascer com um byte a mais do que a aplicação envia. O pior: qualquer comparação
feita a partir do `.env` mostra os dois lados idênticos, porque a leitura já
normalizou — o diagnóstico dá falso negativo.

### 10.5. Script de provisionamento tem que ser atômico fim a fim

`BEGIN`/`COMMIT` **dentro do arquivo**, não `--single-transaction` na invocação.

**Por quê:** o molde tem blocos `DO` separados, que são transações independentes. Uma
falha entre eles deixa a role criada e o `REVOKE CONNECT` sem aplicar. E o
`docker start` seguinte sobe **limpo e silencioso**, porque a imagem oficial só executa
os hooks de initdb com `PGDATA` vazio — ninguém é avisado. A garantia tem que estar no
arquivo porque ele roda por dois caminhos (initdb e `docker exec ... psql -f`), e
depender de quem digita o comando lembrar da flag reabre a janela.

### 10.6. Role é global ao cluster: guarde contra o database errado

O SQL de provisionamento deve abortar, antes de qualquer DDL, se
`current_database()` não for o esperado.

**Por quê:** `ALTER ROLE ... PASSWORD` vale para o cluster inteiro. Rodar o script com
o `-d` errado rotaciona a senha da role de produção em silêncio e ainda dá ownership a
um banco não relacionado. O molde não tem essa guarda.

### 10.7. Em cluster compartilhado, alguém tem que criar o database

O script que provisiona a role assume que o database existe — localmente quem o cria é
o `POSTGRES_DB` do compose, via entrypoint da imagem. **Num cluster que já está de pé
não há entrypoint nenhum.** Providencie o `CREATE DATABASE` num passo separado
(`\gexec`; ele não roda em transação nem em bloco `DO`).

### 10.8. Asserção negativa precisa de controle positivo

Teste que afirma "X não referencia Y" passa tanto quando a regra é respeitada quanto
quando o mecanismo de detecção quebra. Acompanhe-o de uma asserção sobre algo que
**existe de verdade**.

**Por quê:** se `GetReferencedAssemblies()` um dia devolver vazio — outro
TargetFramework, trimming, mudança de SDK —, todos os testes de camada ficam verdes
sem checar nada, e a regra de PADROES §1 fica desprotegida em silêncio. Mesma lógica
para teste que itera coleção: guarde contra a coleção vazia.

### 10.9. Verifique com o comando literal da documentação

Ao validar, rode **exatamente** o comando que está escrito no README — não um
equivalente com variáveis de ambiente na frente.

**Por quê:** o porte perdeu o `Properties/launchSettings.json`, e sem ele `dotnet run`
sobe em `Production`, onde user-secrets não são carregados. Executores e duas revisões
adversariais passaram, porque todos subiam a aplicação com variáveis explícitas. O
comando do README falhava, e quem descobriu foi o dono, na primeira tentativa real.

### 10.10. Compare o molde por AUSÊNCIA, não só por fidelidade

Antes de dar um porte por concluído, liste o que o molde tem e você **não** portou, e
justifique cada ausência.

**Por quê:** fidelidade no que se copia é a metade fácil. Passaram despercebidos o
`launchSettings.json`, a linha `test-results/` do `.gitignore` (removida por parecer
inaplicável, e que voltou a ser aplicável), o CI inteiro, o `sonar-project.properties`
e o gate de cobertura. Todos existiam no molde; nenhum foi decidido, só esquecido.

### 10.11. Endpoint de outro serviço no seu log nunca é ruído

Requisição a um caminho que pertence ao contrato de outro serviço aparecendo no seu
log é sinal de roteamento errado. Investigue antes de explicar como teste manual.

**Por quê:** 16 requisições `GET /v1/titulos` com 404 apareceram no log do Hub e foram
lidas como ruído. Eram o front do `tesouro-direto` caindo aqui por causa da colisão de
alias (10.1) — a prova do incidente estava disponível antes de alguém notar o
incidente.

### 10.12. Runtime dentro de container não enxerga o limite do cgroup

Todo runtime que dimensiona algo "em função da memória disponível" — heap, cache,
watermark, número de threads — lê o **host**, não o seu `deploy.resources.limits`.
Fixe o valor em absoluto, sempre.

**Por quê:** o `vm_memory_high_watermark` do RabbitMQ é relativo por padrão (0,4).
Numa VPS de 2GB isso reserva ~800MB **dentro** de um cgroup de 384MB: o kernel mata o
processo por OOM antes de o watermark ter chance de aplicar backpressure — ou seja, o
mecanismo que existe para evitar o OOM nunca chega a agir. O mesmo vale para o GC do
.NET, que sem limite de cgroup vê os 2GB do host e deixa o heap crescer bem além do
que cresceria vendo 256MB. Corolário desagradável: a env var clássica
`RABBITMQ_VM_MEMORY_HIGH_WATERMARK` está **deprecated** na imagem `rabbitmq:4` e o
entrypoint **recusa subir** com ela setada — o valor absoluto tem que entrar por
arquivo de config.

**Guarda:** depois de subir, confira o valor efetivo lá dentro (`rabbitmqctl status`,
`GC.GetGCMemoryInfo`, o que o runtime expuser). Se ele bate com o host e não com o
container, está errado.

### 10.13. Em host de um núcleo, teto de CPU não contém nada — só corta rajada

Use **peso** (`cpu_shares`) para proteger vizinho sob contenção. Um teto rígido
(`cpus`) abaixo de 1.0 num host de 1 vCPU não impede runaway nenhum, porque o processo
já não pode passar de um núcleo; ele só transforma trabalho curto em trabalho throttled.

**Por quê:** o broker recebeu `cpus: "0.60"` descrito como "folgado, só para conter
loop desgovernado". Não era folgado: a coleta de estatísticas do plugin de management
(a cada 5s por padrão) precisa de ~0,6s de CPU, e o corte a 60ms por período de 100ms
a esticava por ~10 períodos. Resultado: ~20% dos períodos throttled, exatamente o
limiar do alerta, com o broker **99,8% ocioso** no resto do tempo.

**Como reconhecer:** throttling com CPU média baixa é normal e enganoso — o CFS não
limita média, limita fatia por período. Num laço de `docker stats`, rajada que para
**exatamente** no valor da cota é trabalho cortado, não trabalho excessivo.

### 10.14. Serviço novo muda o orçamento dos VIZINHOS, não só o seu

Ao acrescentar serviço ou volume de dados numa máquina pequena, revise os tetos de
quem já estava lá. O limite deles foi dimensionado para um mundo que você acabou de
mudar.

**Por quê:** o Postgres compartilhado tinha 128MB, dimensionados quando servia só o
`tesouro_direto` (44MB). O Hub passou a escrever no mesmo cluster e o database `hub`
chegou a 298MB — 288MB só da tabela `precos`. Servir ~342MB dentro de um cgroup de
128MB faz o kernel reciclar página continuamente, e o alerta de reclaim sustentado
começou a disparar num container que ninguém tinha tocado. A ADR-12 separa **bancos**
por serviço; ela não separa **orçamento de memória**, porque a instância é uma só.

### 10.15. `up -d` retorna quando o container arranca, não quando fica pronto

Qualquer passo de deploy que fale com um serviço recém-subido precisa esperar pela
**condição real** que ele usa — não pelo healthcheck, e nunca por nada.

**Por quê:** a verificação de credencial do broker rodava logo após o `up -d` e falhou
com `curl: (7) failed to connect` 6 segundos depois do "Started", reportando
credencial divergente quando o problema era só tempo. A espera implícita existia antes
por acidente (`depends_on: service_healthy`) e sumiu quando essa condição foi trocada
por `service_started` — por um bom motivo, mas sem que ninguém procurasse quem
dependia dela. Esperar pelo healthcheck também não resolveria:
`rabbitmq-diagnostics check_running` fica verde **antes** de a porta de management
aceitar conexão, então ele é um proxy da condição, não a condição.

**Guarda:** o laço espera pela própria verificação que você vai fazer, e distingue
"não conectou" (repete) de "conectou e recusou" (falha rápido) — são diagnósticos
diferentes, e tratá-los igual manda o operador para o lado errado.

### 10.16. Cache sem prazo é uma afirmação que nunca é confrontada

Toda entrada de cache que descreve estado externo precisa de prazo de validade. Sem
ele, o cache afirma para sempre algo que ele não observa.

**Por quê:** o ETag da TD API ficava num dicionário em memória, sem expiração.
Enquanto o banco só crescia, a afirmação era verdadeira por acidente. No instante em
que o banco foi zerado por fora, ela virou mentira permanente: a sonda mandava
`If-None-Match`, a TD API respondia 304 corretamente ("você já tem esse corpo" — e o
Hub não tinha), e o ciclo encerrava sem ingerir nada, indefinidamente, até alguém
reiniciar o container. É o antipadrão do §9 — estado de controle divergindo dos dados.

**Cuidado com o remédio errado:** persistir o ETag junto dos dados parece "derivar dos
dados" e é pior. A volatilidade foi justamente o que permitiu curar o incidente com um
restart; persistindo, o incidente vira permanente, e continua quebrando em truncate
parcial, restore de dump antigo e delete de uma tabela só.

### 10.17. Commit mergeado não é commit em produção

Se o job de deploy depende do job de teste, um teste instável **pula o deploy em
silêncio**. O painel do repositório fica verde-ish, a `main` tem o código, e produção
não tem.

**Por quê:** um PR ficou 12 dias mergeado e fora de produção. O teste falhou por
instabilidade de infraestrutura, o deploy foi pulado, e ninguém notou — porque nada
distingue "nunca deployado" de "deployado" na leitura casual do histórico.

**Guarda:** o pipeline deve avisar quando a `main` tem commit sem deploy
correspondente. Enquanto isso não existir, confira o último run de `push` depois de
todo merge, não só o do PR.
