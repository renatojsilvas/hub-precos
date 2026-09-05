# Dashboard e alertas do Hub

`dashboards/hub-precos.json` — painel do Grafana Cloud para este serviço.
`cloud/rules.yaml` — regras de alerta do Hub, publicadas no Grafana Cloud.

## Por que os arquivos moram aqui e são aplicados de outro repo

O dashboard e as regras descrevem o Hub, então é aqui que devem ser versionados: quem
muda uma métrica do Hub tem que ver o painel (ou o alerta) quebrar no mesmo diff.

Mas quem publica é o `scripts/grafana-cloud/apply-cloud.sh`, que vive no repo
`tesouro-direto-api` — ele lê `infra/grafana/` daquele repo e converge por API
(inclusive apagando da nuvem o que sai da fonte). Enquanto não houver um mecanismo de
publicação próprio, aplicar exige copiar os arquivos para lá:

```
cp infra/grafana/dashboards/hub-precos.json ../tesouro-direto-api/infra/grafana/dashboards/
cp infra/grafana/cloud/rules.yaml ../tesouro-direto-api/infra/grafana/cloud/rules-hub.yaml
cd ../tesouro-direto-api && ./scripts/grafana-cloud/apply-cloud.sh
```

Duplicação consciente, e o custo é real: as duas cópias divergem em silêncio. Ao mexer
nestes arquivos, copie de novo — ou o painel/alerta na nuvem descreve uma versão que não
existe mais.

**O nome muda ao copiar `rules.yaml`.** O `apply-cloud.sh` já tem um bloco dedicado que
lê `infra/grafana/cloud/rules-hub.yaml` — não `rules.yaml` — porque o TD já usa esse
nome lá para as próprias 21 regras. Copiar como `rules.yaml` faria o PUT do publicador
sobrescrever as regras do TD com só as duas do Hub, apagando as outras 19 da nuvem em
silêncio. `rules-hub.yaml` vai para uma pasta e grupo próprios (`HubPrecos` /
`hub-alertas`), então não colide com `tesouro-alertas` mesmo que algum dia troquem de
nome de arquivo pelo de grupo.

## O que o dashboard mostra, e o que mudou

Além das métricas de infraestrutura (HTTP, pool do Npgsql, memória, CPU, GC), o painel
agora tem métricas de NEGÓCIO: frescor da ingestão, último preço novo, ciclos por
desfecho, preços por tipo e instrumentos com falha. Elas existem por um motivo concreto:
a ingestão em produção falhou a cada 15min por dias, com as tabelas em 0 linhas, e nada
avisou — os painéis técnicos (up, health check, CPU) ficavam todos verdes o tempo
inteiro, porque o processo em si nunca caiu.

Dois painéis carregam contexto que não é óbvio pelo número:

- **Pool de conexões** — o teto de 5 é orçamento, não performance: o cluster é dividido
  com `td_api`, `custodia` e `operacoes`. Encostar no teto é motivo para rever o
  orçamento do cluster, não só para subir o número.
- **Memória** — este container não tem limite. Crescimento sustentado aqui derruba o
  vizinho numa VPS de 2GB, não o Hub. É o painel para olhar antes de decidir o teto.

E dois painéis de negócio merecem a mesma nota:

- **Frescor da ingestão** vs. **Último preço novo** — são coisas diferentes de
  propósito. Frescor mede se o ciclo terminou sem erro; preço novo mede se o ciclo
  trouxe dado. Um upstream que responde 200 com CSV vazio faz o primeiro ficar verde e
  o segundo ficar vermelho — é exatamente a classe de falha silenciosa que a métrica
  única de "sucesso" não pegava.
- **Instrumentos com falha** é acumulado por `increase`, não instantâneo: falha parcial
  dentro de um ciclo "success" nunca aparecia nos logs de resumo antes desta métrica.

Quatro painéis novos cobrem o relay outbox → RabbitMQ: **Backlog da outbox** (contagem,
`hub_outbox_pendentes`), **Idade do backlog mais antigo** (`hub_outbox_pendente_mais_antiga_segundos`,
a métrica que os alertas usam — contagem sobe e desce o tempo todo, transitório é
normal; idade alta é o sintoma real), **Ciclos de relay por desfecho** e **Eventos
publicados no broker**.

## Regras de alerta (`cloud/rules.yaml`)

Cinco regras, grupo `hub-alertas`, pasta `HubPrecos`:

- **Hub — Serviço sumiu do scrape**: `absent(up{job="hub-precos"})`, `for: 10m`. É a
  regra que vigia a própria observabilidade — a única que dispara quando o problema é
  NÃO haver dado. Todas as outras dependem de métricas do Hub existirem; se a série
  some, elas ficam mudas, e ausência de alerta é lida como "está tudo bem". Foi assim
  que o Hub passou semanas sem alerta nenhum na nuvem. `absent()` e não `up == 0`
  porque `up == 0` só existe se o alvo ainda estiver configurado e falhando: quem
  remove o alvo do scrape ou renomeia o container faz a série sumir, e `up == 0` nunca
  fica verdadeiro. `noDataState: OK` ao contrário das outras — `absent()` já É o sinal,
  e marcar no-data como Alerting misturaria "o Hub sumiu" com "o Grafana não
  respondeu", que têm donos diferentes.

- **Hub — Falha de ingestão**: mais de 3 ciclos de falha em 1h (metade dos 4 ciclos/hora
  do cron de 15min). `[1h] > 3`, não `[6h] > 0` como o TD, porque o TD importa 1x/dia e
  o Hub roda 96x/dia — `> 0` viraria ruído constante.
- **Hub — Dado parado**: `time() - max(hub_ingestao_ultimo_sucesso_timestamp_seconds) >
  3600`, `for: 30m`. Dois desvios deliberados do molde do TD, mais uma folga na janela:
  - Sem guarda de `day_of_week`: o TD só publica em dia útil, o Hub roda 24/7. Copiar a
    guarda criaria cegueira de fim de semana.
  - `noDataState: Alerting` (o TD usa `OK`): se o app reinicia e nunca mais tem sucesso,
    o gauge some e a expressão vira no-data. Com `OK` o alerta calaria — o exato defeito
    de produção que motivou esta tarefa.
  - `for: 30m`, não `15m`: a margem tem que ser sobre a CADÊNCIA de quem alimenta a
    métrica, não igual a ela. O cron é `0 0/15 * * * ?` com
    `WithMisfireHandlingInstructionDoNothing()` (`DependencyInjection.cs`) — um restart
    de container que atravesse um tick agendado pula aquele tick e só dispara no
    próximo, um gap real de ~30min em deploy saudável. Somado a `noDataState: Alerting`
    (o gauge some no restart), `for: 15m` dispararia em todo deploy — o alerta grita
    lobo e destrói a própria credibilidade que a métrica existe para sustentar. Compare
    com `td-metricas-container-obsoletas` do repo de referência: `for: 5m` para uma
    métrica alimentada a cada ~1min, 5x de folga sobre a cadência. A cadência aqui é
    15min, então a folga proporcional pede dezenas de minutos — 30m cobre um tick
    perdido mais um misfire. Não aperte este valor de volta para 15m achando frouxo.
- **Hub — Backlog da outbox envelhecido**: `max(hub_outbox_pendente_mais_antiga_segundos)
  > 900` (15min), `for: 5m`, `noDataState: Alerting`. Idade, não contagem
  (`hub_outbox_pendentes`) de propósito — backlog transitório é normal (o relay drena a
  cada 5s), backlog VELHO é o sintoma. `noDataState: Alerting` pelo mesmo motivo da regra
  de frescor: esta métrica só é gravada quando `RelayOutboxJob` termina um ciclo com
  sucesso (`RelayResultado.PendentesRestantes` não nulo); ausência prolongada significa
  que o relay nunca leu o backlog desde o boot — app fora do ar ou Postgres inacessível.
- **Hub — Relay outbox falhando persistentemente**:
  `increase(hub_relay_ciclos_total{outcome="failure"}[5m]) > 30`, `for: 5m`,
  `noDataState: OK`. Existe porque a regra de idade do backlog acima NÃO cobre todo
  cenário: quando o próprio ciclo do relay falha (RabbitMQ fora do ar, Postgres
  inacessível), `RelayOutboxJob` só chama `RecordCicloRelay("failure")` —
  `RecordOutboxBacklog` nunca é chamado nesse caminho, então o gauge de idade CONGELA no
  último valor bom em vez de crescer. As duas regras juntas cobrem broker/DB fora do ar
  (esta) e relay que lê mas não consegue drenar no ritmo (a de idade).

Sem `contactpoints.yaml` nem `policies.yaml` neste repo: quem define o roteamento do
Telegram é o repo de referência. Lá existem dois contact points para o MESMO bot e o
MESMO chat id — `telegram-tesouro` e `telegram-hub` — diferindo só no `message`, que
prefixa a origem (🟢 TESOURO DIRETO / 🔵 HUB DE PRECOS), para dar pra saber de qual
serviço veio o alerta sem abrir o Grafana. O `policies.yaml` de lá tem uma rota FILHA
casando `service = hub-precos` → `telegram-hub`; a raiz continua em `telegram-tesouro`,
byte a byte igual a antes.

**O label `service: hub-precos` destas regras virou contrato.** É ele que a rota filha
casa lá no repo de referência. Quem remover ou renomear esse label aqui (nas duas regras
deste arquivo) quebra o roteamento do lado de lá, sem erro visível na hora — o YAML
continua válido, o `apply-cloud.sh` continua aplicando com sucesso, só o Telegram é que
passa a rotular errado.

**O modo de falha se o label não casar não é silêncio.** Roteamento do Alertmanager cai
para o pai quando nenhuma rota filha casa, então o alerta do Hub ainda chega — só que
pelo `telegram-tesouro`, com o prefixo do TD. O risco de um label divergente é mensagem
mal identificada, não alerta perdido. Vale saber disso antes de sair caçando alerta
sumido: se o Hub disparou e a mensagem apareceu com 🟢 TESOURO DIRETO, o alerta chegou —
só o roteamento que desalinhou.

## O relay outbox → RabbitMQ está ligado em produção — as duas regras de outbox passam a valer de verdade

Até aqui, **Hub — Backlog da outbox envelhecido** e **Hub — Relay outbox falhando
persistentemente** eram regras publicadas mas sem sinal real por trás: o broker não
existia em produção, `Outbox:Relay:AgendamentoAtivo` nascia `false` lá, e as métricas
que essas duas regras leem (`hub_outbox_pendente_mais_antiga_segundos`,
`hub_relay_ciclos_total{outcome="failure"}`) nunca eram gravadas fora do ambiente
local. Com o broker `plataforma-rabbitmq` no ar em produção e o relay ligado por
padrão (`HUB_RELAY_ATIVO=true`), as duas passam a ter dado de verdade por trás —
inclusive o pico transitório esperado no primeiro deploy, quando o relay drena de
uma vez o backlog acumulado desde a fase de ingestão (ver seção "Relay outbox →
RabbitMQ" do `README.md` da raiz).

Isso não muda nada no fluxo de publicação: continua sendo o passo manual de copiar
`cloud/rules.yaml` para `../tesouro-direto-api/infra/grafana/cloud/rules-hub.yaml` e
rodar `apply-cloud.sh` de lá (ver seção acima) — só reforça que, a partir de agora,
publicar essas regras sem o relay realmente ligado do outro lado significa publicar
alerta cego (sempre `OK`/sem dado), e não publicá-las com o relay já ligado significa
não ter alerta nenhum enquanto o backlog cresce em silêncio. Esta tarefa (habilitação
em produção) **não** executou `apply-cloud.sh` nem copiou nada para o
`tesouro-direto-api` — isso continua sendo o passo manual descrito acima, de
responsabilidade de quem decidir publicar.
