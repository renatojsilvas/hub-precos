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

## Regras de alerta (`cloud/rules.yaml`)

Duas regras, grupo `hub-alertas`, pasta `HubPrecos`:

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

Sem `contactpoints.yaml` nem `policies.yaml` neste repo: as regras do Hub reusam o
contact point `telegram-tesouro` do repo de referência por herança da policy raiz (que
não tem rota por label), então já caem no mesmo Telegram sem precisar de configuração
própria. Mexer em `policies.yaml` arriscaria silenciar o TD.
