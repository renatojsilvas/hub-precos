# Dashboard do Hub

`dashboards/hub-precos.json` — painel do Grafana Cloud para este serviço.

## Por que o arquivo mora aqui e é aplicado de outro repo

O dashboard descreve o Hub, então é aqui que ele deve ser versionado: quem muda uma
métrica do Hub tem que ver o painel quebrar no mesmo diff.

Mas quem o publica é o `scripts/grafana-cloud/apply-cloud.sh`, que vive no repo
`tesouro-direto` — ele lê `infra/grafana/dashboards/` daquele repo e converge por API
(inclusive apagando da nuvem o que sai da fonte). Enquanto não houver um mecanismo de
publicação próprio, aplicar exige copiar este arquivo para lá:

```
cp infra/grafana/dashboards/hub-precos.json ../tesouro-direto-api/infra/grafana/dashboards/
cd ../tesouro-direto-api && ./scripts/grafana-cloud/apply-cloud.sh
```

Duplicação consciente, e o custo é real: as duas cópias divergem em silêncio. Ao mexer
neste arquivo, copie de novo — ou o painel na nuvem descreve uma versão que não existe
mais.

## O que ele mostra, e o que ainda não

Todas as 13 séries usadas foram conferidas contra o `/metrics` real do container em
produção. São métricas de infraestrutura (HTTP, pool do Npgsql, memória, CPU, GC) —
**não há métrica de negócio**, porque não há negócio ainda. Quando a ingestão existir,
os painéis que importam de verdade (frescor do dado, sucesso do último job, profundidade
de fila) entram aqui.

Dois painéis carregam contexto que não é óbvio pelo número:

- **Pool de conexões** — o teto de 5 é orçamento, não performance: o cluster é dividido
  com `td_api`, `custodia` e `operacoes`. Encostar no teto é motivo para rever o
  orçamento do cluster, não só para subir o número.
- **Memória** — este container não tem limite. Crescimento sustentado aqui derruba o
  vizinho numa VPS de 2GB, não o Hub. É o painel para olhar antes de decidir o teto.
