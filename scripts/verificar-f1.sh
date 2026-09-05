#!/usr/bin/env bash
# Verifica as cinco provas de pronto do F1 (ver LEIA-ME-KIT.md, "O que o F1 tem que
# alcancar"). Existe porque prosa nao e verificacao: no hub eu declarei observabilidade
# pronta com os arquivos nunca copiados para o repo do tesouro-direto, e o publicador
# pulava o bloco em silencio. Alerta ficou semanas sem existir na nuvem.
#
# QUANDO RODAR
#   - uma vez, ao FECHAR o F1, antes de marcar o checkbox;
#   - quando desconfiar que a fiacao de observabilidade quebrou.
#
# NAO e monitoramento continuo. O que precisa ser continuo e ALERTA, nao script:
# a regra "sem metrica" (hub-sem-metrica em infra/grafana/cloud/rules.yaml) cobre o
# caso da serie sumir depois. Um script diz "existe hoje"; so o alerta diz "sumiu".
#
# Uso:
#   ./scripts/verificar-f1.sh [nome-do-job]     # default: nome do diretorio do repo
#
# Variaveis (opcionais — o que faltar vira SKIP, com a instrucao do que fazer):
#   GC_GRAFANA_URL, GC_GRAFANA_TOKEN   provas 3 e 4 (metrica e dashboard na nuvem)
#   VPS                                prova 2 via ssh (ex.: root@meu-host)
#   REPO_TD                            checagem da fiacao (default: ../tesouro-direto-api)
set -uo pipefail

NOME="${1:-$(basename "$(cd "$(dirname "$0")/.." && pwd)")}"
REPO_TD="${REPO_TD:-$(cd "$(dirname "$0")/../.." && pwd)/tesouro-direto-api}"
ok=0; falha=0; pulado=0

verde()   { printf '  \033[32mOK\033[0m    %s\n' "$1"; ok=$((ok+1)); }
vermelho(){ printf '  \033[31mFALHA\033[0m %s\n' "$1"; falha=$((falha+1)); }
amarelo() { printf '  \033[33mSKIP\033[0m  %s\n' "$1"; pulado=$((pulado+1)); }

echo "== provas de pronto do F1 — servico '$NOME' =="
echo

# --- 1. um merge na main deploya sozinho ----------------------------------------
# Nao basta o CI estar verde: se o job de deploy depende do de teste, um teste
# instavel PULA o deploy em silencio (PADROES 10.17 — no hub um PR ficou 12 dias
# mergeado e fora do ar assim). Por isso olhamos o run de PUSH, e o job de deploy
# dentro dele.
echo "1. merge na main deploya sozinho"
origem=$(git config --get remote.origin.url 2>/dev/null || true)
# sed basico de proposito: o BSD sed do macOS nao aceita quantificador lazy
slug=$(printf '%s' "$origem" | sed -e 's#\.git$##' -e 's#^.*[:/]\([^/]*\)/\([^/]*\)$#\1/\2#')
if [ -z "$slug" ]; then
  amarelo "sem remote origin — rode dentro do repo do servico"
else
  runs=$(curl -sf "https://api.github.com/repos/$slug/actions/runs?event=push&branch=main&per_page=1" 2>/dev/null || true)
  concl=$(printf '%s' "$runs" | python3 -c "
import sys,json
try:
    r=json.load(sys.stdin)['workflow_runs'][0]
    print(r['conclusion'] or r['status'], r['head_sha'][:7])
except Exception: print('')" 2>/dev/null)
  if [ -z "$concl" ]; then
    amarelo "nao consegui ler os runs de '$slug' (repo privado? sem rede?)"
  else
    estado=${concl%% *}; sha=${concl##* }
    if [ "$estado" = "success" ]; then
      jobs=$(curl -sf "https://api.github.com/repos/$slug/actions/runs?event=push&branch=main&per_page=1" | python3 -c "
import sys,json;print(json.load(sys.stdin)['workflow_runs'][0]['id'])" 2>/dev/null)
      dep=$(curl -sf "https://api.github.com/repos/$slug/actions/runs/$jobs/jobs" | python3 -c "
import sys,json
js=json.load(sys.stdin)['jobs']
d=[j for j in js if 'deploy' in j['name'].lower()]
print(d[0]['conclusion'] if d else 'ausente')" 2>/dev/null)
      case "$dep" in
        success) verde "run de push $sha: deploy executou e passou" ;;
        skipped) vermelho "run de push $sha esta verde MAS o job de deploy foi PULADO — verde no CI nao e deployado (10.17)" ;;
        ausente) vermelho "nao ha job de deploy no workflow — o F1 exige merge deployando sozinho" ;;
        *)       vermelho "job de deploy do run $sha: $dep" ;;
      esac
    else
      vermelho "ultimo run de push na main: $estado ($sha)"
    fi
  fi
fi

# --- 2. health responde PELA VPS ------------------------------------------------
# Pela VPS de proposito: responder no seu laptop nao prova nada sobre producao.
echo
echo "2. /health/ready responde pela VPS"
if [ -n "${VPS:-}" ]; then
  st=$(ssh -o BatchMode=yes -o ConnectTimeout=8 "$VPS" \
       "docker exec ${NOME}-app curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/health/ready" 2>/dev/null || true)
  [ "$st" = "200" ] && verde "200 em ${NOME}-app" || vermelho "codigo '$st' (esperado 200)"
else
  amarelo "defina VPS=user@host, ou rode la: docker exec ${NOME}-app curl -sf http://localhost:8080/health/ready"
fi

# --- 3 e 4. metrica e dashboard na nuvem ----------------------------------------
echo
echo "3/4. serie e dashboard no Grafana Cloud"
if [ -z "${GC_GRAFANA_URL:-}" ] || [ -z "${GC_GRAFANA_TOKEN:-}" ]; then
  amarelo "defina GC_GRAFANA_URL e GC_GRAFANA_TOKEN (token de service account com leitura)"
else
  gc() { curl -sf -H "Authorization: Bearer $GC_GRAFANA_TOKEN" "$GC_GRAFANA_URL$1" 2>/dev/null; }
  # o uid do datasource nao pode ser escolhido por posicao: a stack tem datasources
  # META do Grafana Cloud (grafanacloud-usage) que vem antes dos nossos.
  ds=$(gc /api/datasources | python3 -c "
import sys,json
l=[d for d in json.load(sys.stdin) if d['type']=='prometheus' and 'usage' not in d.get('name','')]
print(l[0]['uid'] if l else '')" 2>/dev/null)
  if [ -z "$ds" ]; then
    vermelho "datasource prometheus nao encontrado na stack"
  else
    n=$(gc "/api/datasources/proxy/uid/$ds/api/v1/query?query=up%7Bjob%3D%22$NOME%22%7D" \
        | python3 -c "import sys,json;print(len(json.load(sys.stdin)['data']['result']))" 2>/dev/null)
    [ "${n:-0}" -gt 0 ] 2>/dev/null \
      && verde "serie up{job=\"$NOME\"} presente na nuvem" \
      || vermelho "nenhuma serie com job=\"$NOME\" — o alloy do tesouro-direto nao esta raspando este servico"
    # Busca pelo UID, nao pelo titulo. O /api/search casa SO por titulo, e o titulo
    # e legivel enquanto o uid e o slug do servico: medido em 2026-09-05, query=operacoes
    # devolvia 0 com o dashboard publicado (titulo "Operacoes" com cedilha e til), e o
    # mesmo valia para hub-precos ("Hub de Precos") e tesouro-direto-api ("Tesouro
    # Direto API") — ou seja, esta checagem era falso negativo para TODOS os servicos da
    # plataforma, e uma prova que nunca passa nao prova nada. O uid e o dado estavel: e
    # ele que o apply-cloud.sh publica e que os links do dashboard usam.
    d=$(gc "/api/dashboards/uid/$NOME" >/dev/null 2>&1 && echo 1 || echo 0)
    [ "${d:-0}" -gt 0 ] 2>/dev/null \
      && verde "dashboard com '$NOME' publicado" \
      || vermelho "nenhum dashboard com uid='$NOME' na nuvem — copiou o JSON E pos o nome na lista do apply-cloud.sh?"
  fi
fi

# --- fiacao no repo do tesouro-direto -------------------------------------------
# Metrica e PULL: o alvo do scrape e o dashboard moram la, nao aqui. Foi exatamente
# aqui que o hub perdeu semanas — os arquivos nunca tinham sido copiados e o
# publicador pulava o bloco avisando "ausente" no meio de uma saida longa.
echo
echo "fiacao no repo do tesouro-direto ($REPO_TD)"
if [ ! -d "$REPO_TD" ]; then
  amarelo "repo nao encontrado — defina REPO_TD=/caminho/do/tesouro-direto-api"
else
  grep -q "job = \"$NOME\"\|job=\"$NOME\"" "$REPO_TD/infra/alloy/config.alloy" 2>/dev/null \
    && verde "alvo de scrape com job=$NOME em config.alloy" \
    || vermelho "sem alvo de scrape para '$NOME' em infra/alloy/config.alloy"
  [ -f "$REPO_TD/infra/grafana/dashboards/$NOME.json" ] \
    && verde "dashboard $NOME.json presente" \
    || vermelho "falta infra/grafana/dashboards/$NOME.json"
  grep -q "$NOME" "$REPO_TD/scripts/grafana-cloud/apply-cloud.sh" 2>/dev/null \
    && verde "'$NOME' citado no apply-cloud.sh" \
    || vermelho "'$NOME' NAO esta no apply-cloud.sh — copiar o JSON nao basta, o nome entra na lista"
  # O nome do arquivo de regras nao e derivavel do nome do servico: o hub usa
  # 'rules-hub.yaml' e o repo se chama 'hub-precos'. O que importa e que o
  # apply-cloud.sh leia ALGUM rules-*.yaml e que esse arquivo exista.
  ref=$(grep -oE "rules-[a-z0-9-]+\.yaml" "$REPO_TD/scripts/grafana-cloud/apply-cloud.sh" 2>/dev/null | sort -u)
  if [ -z "$ref" ]; then
    vermelho "o apply-cloud.sh nao le nenhum rules-*.yaml de servico vizinho"
  else
    for r in $ref; do
      [ -f "$REPO_TD/infra/grafana/cloud/$r" ] \
        && verde "$r presente e lido pelo apply-cloud.sh" \
        || vermelho "o apply-cloud.sh le '$r', mas o arquivo nao existe — as regras sao puladas em silencio"
    done
  fi
fi

# --- 5. alerta chegando no Telegram ---------------------------------------------
echo
echo "5. alerta chega no Telegram"
amarelo "so se prova disparando um de proposito — nenhum script substitui isso"
echo "      Baixe o limiar de uma regra sua, espere o disparo, confirme no chat, restaure."
echo "      E a unica prova da corrente inteira: regra publicada, avaliando, contact"
echo "      point com token bom e roteamento funcionando."

echo
echo "== $ok ok, $falha falha(s), $pulado pulado(s) =="
[ "$falha" -eq 0 ] || { echo "O F1 NAO fechou. Nao marque o checkbox." >&2; exit 1; }
[ "$pulado" -eq 0 ] || { echo "Sem falha, mas ha prova nao verificada — resolva os SKIP antes de fechar." >&2; exit 2; }
echo "Todas as provas automatizaveis passaram."
