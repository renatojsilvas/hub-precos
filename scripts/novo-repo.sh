#!/usr/bin/env bash
# Prepara um repo novo da plataforma (operacoes, custodia) a partir deste.
#
# POR QUE COPIAR DAQUI, E NAO DO MOLDE: o `tesouro-direto-api` NAO tem as guardas
# da secao 10 do PADROES — conferido: nem a de colisao de alias DNS, nem a
# verificacao de credencial pela rede. Elas nasceram de incidentes deste repo.
# Entao a divisao e:
#
#   - CODIGO (4 projetos, Result, MapReadGet, middlewares) -> portar do MOLDE,
#     seguindo a regra de ouro do CLAUDE.md. Este script NAO gera codigo.
#   - KIT, INFRA e CI -> copiar DAQUI, que e onde as licoes estao.
#
# Uso:
#   ./scripts/novo-repo.sh operacoes Operacoes [destino]
#
#   $1 nome do repo/servico/role/database (minusculo, kebab)
#   $2 prefixo dos projetos .NET (PascalCase)
#   $3 destino (default: ../$1)
set -euo pipefail

NOME="${1:?uso: novo-repo.sh <nome> <PrefixoDotNet> [destino]}"
PREFIXO="${2:?uso: novo-repo.sh <nome> <PrefixoDotNet> [destino]}"
ORIGEM="$(cd "$(dirname "$0")/.." && pwd)"
DESTINO="${3:-$(dirname "$ORIGEM")/$NOME}"
ENVPREFIXO="$(echo "$NOME" | tr '[:lower:]-' '[:upper:]_')"

echo "== plano =="
echo "  origem ............ $ORIGEM"
echo "  destino ........... $DESTINO"
echo "  servico/role/db ... $NOME"
echo "  projetos .NET ..... $PREFIXO.API, $PREFIXO.Application, $PREFIXO.Domain, $PREFIXO.Infrastructure"
echo "  prefixo de env .... ${ENVPREFIXO}_"
echo

# Destino aceito: inexistente, vazio, ou um repo recem-criado no GitHub e clonado —
# que e o caso NORMAL (.git mais os arquivos que o GitHub cria sozinho). Qualquer outra
# coisa la dentro e trabalho de alguem, e este script nao sobrescreve nada.
if [ -e "$DESTINO" ]; then
  inesperado=$(ls -A "$DESTINO" 2>/dev/null \
    | grep -vxE '\.git|README\.md|readme\.md|LICENSE|LICENSE\.md|\.gitignore|\.gitattributes' || true)
  if [ -n "$inesperado" ]; then
    echo "ERRO: '$DESTINO' tem conteudo que nao e de repo recem-criado:" >&2
    echo "$inesperado" | sed 's/^/  /' >&2
    echo "Este script nao sobrescreve nada. Esvazie o destino ou aponte para outro." >&2
    exit 1
  fi
  echo "  (destino e um repo ja clonado: $(ls -A "$DESTINO" | tr '\n' ' '))"
  echo
fi

mkdir -p "$DESTINO"

# --- A. o kit: vai sem alteracao de conteudo -------------------------------------
# PADROES.md e LEIA-ME-KIT.md sao IDENTICOS entre os repos de proposito: e isso que
# faz a manutencao ser em um arquivo so. Nao os edite aqui.
echo "== A. kit =="
mkdir -p "$DESTINO/.claude"
cp "$ORIGEM/PADROES.md" "$ORIGEM/LEIA-ME-KIT.md" "$DESTINO/"
cp -R "$ORIGEM/.claude/agents" "$DESTINO/.claude/"
cp "$ORIGEM/CLAUDE.md" "$DESTINO/CLAUDE.md"
echo "  PADROES.md, LEIA-ME-KIT.md, .claude/agents/ (5), CLAUDE.md"

# --- B. infra e CI: copiados DAQUI porque carregam as guardas da secao 10 ---------
echo "== B. infra e CI (com as guardas da secao 10) =="
for f in .github/workflows/ci.yml \
         infra/postgres/provision-remote.sh \
         infra/postgres/sql/hub-role.sql \
         infra/postgres/initdb/01-provision-hub.sh \
         infra/postgres/README.md \
         docker-compose.yml docker-compose.prod.yml \
         Dockerfile .dockerignore .gitignore \
         Directory.Build.props sonar-project.properties \
         .env.example \
         scripts/coverage-gate.py \
         docs/ROADMAP.md; do
  [ -f "$ORIGEM/$f" ] || { echo "  AVISO: '$f' nao existe na origem, pulando"; continue; }
  mkdir -p "$DESTINO/$(dirname "$f")"
  cp "$ORIGEM/$f" "$DESTINO/$f"
done
# nomes de arquivo que carregam o nome do servico
mv "$DESTINO/infra/postgres/sql/hub-role.sql" "$DESTINO/infra/postgres/sql/$NOME-role.sql"
mv "$DESTINO/infra/postgres/initdb/01-provision-hub.sh" "$DESTINO/infra/postgres/initdb/01-provision-$NOME.sh"
chmod +x "$DESTINO/infra/postgres/provision-remote.sh" "$DESTINO/infra/postgres/initdb/01-provision-$NOME.sh"
echo "  ci.yml, provisionamento do Postgres, composes, Dockerfile, gates, ROADMAP"

# --- substituicoes ---------------------------------------------------------------
# Ordem importa: 'hub-precos' antes de 'hub', senao 'hub-precos' vira '<nome>-precos'.
# PADROES.md e LEIA-ME-KIT.md sao EXCLUIDOS de proposito: eles tem que sair byte a byte
# iguais aos daqui (a verificacao la embaixo cobra isso). Substituir dentro deles trocaria
# os exemplos concretos dos incidentes do hub — que sao justamente o que da forca ao texto —
# por nomes de um servico onde nada daquilo aconteceu.
echo "== substituicoes =="
find "$DESTINO" -type f \( -name '*.md' -o -name '*.yml' -o -name '*.yaml' -o -name '*.sh' \
     -o -name '*.sql' -o -name '*.json' -o -name '*.props' -o -name '*.properties' \
     -o -name '*.py' \
     -o -name 'Dockerfile' -o -name '.env.example' -o -name '.gitignore' -o -name '.dockerignore' \) \
     ! -name 'PADROES.md' ! -name 'LEIA-ME-KIT.md' \
     -print0 | while IFS= read -r -d '' f; do
  perl -pi -e "
    s/hub-precos/$NOME/g;
    s/HUB_/${ENVPREFIXO}_/g;
    s/\bHub\./$PREFIXO./g;
    s/\bHub\.sln\b/$PREFIXO.sln/g;
    s/\bhub\b/$NOME/g;
    s/\bHUB\b/$ENVPREFIXO/g;
  " "$f"
done
echo "  hub-precos -> $NOME | HUB_ -> ${ENVPREFIXO}_ | Hub. -> $PREFIXO. | hub -> $NOME"

# --- banner nos composes: eles sao REFERENCIA, nao drop-in -----------------------
# Os composes carregam decisoes especificas do hub que um sed nao sabe traduzir: o
# servico do broker (que este repo NAO deve subir), os limites de recurso escolhidos
# para a carga do hub, e comentarios em prosa onde "Hub" as vezes e auto-referencia
# (vira o seu servico) e as vezes e historico (fica como esta). Marcar em vez de
# adivinhar.
for c in docker-compose.yml docker-compose.prod.yml; do
  [ -f "$DESTINO/$c" ] || continue
  tmp="$DESTINO/.$c.tmp"
  cat > "$tmp" <<'BANNER'
# =============================================================================
# ADAPTE ANTES DE USAR — copiado do hub-precos, nao e drop-in.
#
# 1. REMOVA o servico `plataforma-rabbitmq`. O broker e da PLATAFORMA e ja esta
#    de pe; este repo so precisa das env vars de cliente (RabbitMq__Host etc.).
#    Subir um segundo com o mesmo nome e a colisao de alias da secao 10.1.
# 2. DECIDA os limites de recurso. Os numeros aqui sao da carga do hub, medidos
#    para ela. A VPS tem 2GB e UM nucleo: servico novo muda o teto dos VIZINHOS
#    (secao 10.14), entao revise os outros no mesmo movimento.
# 3. REVISE os comentarios: "Hub" as vezes e auto-referencia (troque pelo nome
#    deste servico) e as vezes e historico (mantenha — o incidente foi la).
# 4. Apague este banner quando terminar.
# =============================================================================
BANNER
  cat "$DESTINO/$c" >> "$tmp"
  mv "$tmp" "$DESTINO/$c"
done
echo "  banner de adaptacao adicionado aos dois composes"

# --- C. o que NAO vem, e por que -------------------------------------------------
cat > "$DESTINO/COMECE-AQUI.md" <<MARCADOR
# Comece aqui — $NOME

Repo preparado por \`scripts/novo-repo.sh\` do \`hub-precos\` em $(date +%Y-%m-%d).
O kit, a infra e o CI ja vieram **com as guardas da secao 10 do PADROES**, que o repo
de referencia nao tem. Falta o que um script nao pode fazer.

## 1. Leia antes de despachar o primeiro executor

- \`PADROES.md\` secao 10 — cada item nasceu de um incidente real.
- \`LEIA-ME-KIT.md\`, secao **"O que o F1 tem que alcancar"** — o criterio de pronto
  desta primeira fase. Ele **nao** termina quando compila.
- \`LEIA-ME-KIT.md\`, secao **"Erros de orquestracao"** — se voce vai conduzir os
  agents, e ali que voce vai errar.

## 2. Ainda por fazer neste repo

- [ ] \`git init\` e primeiro commit; criar o repo no GitHub.
- [ ] Clonar o molde como irmao: \`git clone <tesouro-direto> ../tesouro-direto-api\`
      (o nome do diretorio precisa ser exatamente esse) e \`/add-dir\` na sessao.
- [ ] **Portar o CODIGO do molde** (nao deste repo): os 4 projetos, \`Result\`/\`Error\`,
      \`ResultExtensions\`, \`MapReadGet\`, \`ApiKeyMiddleware\`, CorrelationId, Serilog.
      Regra de ouro do \`CLAUDE.md\`: localize o equivalente no molde e siga.
- [ ] Copiar \`tests/*.Architecture.Tests/\` do \`hub-precos\` — a versao de la tem o
      **controle positivo** da secao 10.8, que o molde nao tem.
- [ ] \`$PREFIXO.sln\` e os csproj.
- [ ] Reescrever o \`README.md\`.
- [ ] Revisar \`docs/ROADMAP.md\`: veio como template, com a fila do hub.
- [ ] Conferir \`.env.example\` e os composes: os nomes foram substituidos, mas os
      **valores** (portas, limites de recurso) sao do hub e precisam de decisao.

## 3. Fora deste repo

- [ ] Secrets no GitHub: \`VPS_HOST\`, \`VPS_USER\`, \`VPS_SSH_KEY\` e os do servico.
      Cadastre **antes** do primeiro merge — o deploy falha cedo e com mensagem clara
      se faltarem, mas falha.
- [ ] No repo do \`tesouro-direto\`, para metrica (que e *pull* e mora la):
      alvo do scrape em \`infra/alloy/config.alloy\` com \`job=$NOME\`;
      dashboard em \`infra/grafana/dashboards/\`;
      **o nome do dashboard na lista fixa do \`apply-cloud.sh\`** (copiar o JSON nao basta);
      regras como \`rules-$NOME.yaml\`, **nunca** \`rules.yaml\`.
- [ ] Rodar o \`apply-cloud.sh\` com \`GC_GRAFANA_URL\`, \`GC_GRAFANA_TOKEN\` e
      \`TELEGRAM_BOT_TOKEN\` **exportados na invocacao** — o script nao le o \`.env\`, e a
      guarda \\\`\\\${VAR:?}\\\` so testa vazio: um placeholder passa por ela e cala o Telegram
      de todos os servicos, com o script reportando sucesso.
- [ ] **Orcamento de memoria da VPS.** Ela tem 2GB e UM nucleo, ja dividida entre TD,
      Hub e broker. Servico novo muda o teto dos VIZINHOS, nao so o seu (secao 10.14).
      Decida o teto deste antes do primeiro deploy, e revise os outros.
- [ ] Semear a memoria do projeto: ela e **por caminho**, entao este repo nasce com a
      dele vazia. Peca: *"leia o LEIA-ME-KIT.md e grave na memoria do projeto: o
      criterio de pronto do F1, onde ficam as licoes aprendidas, e os limites de
      recurso da VPS"*.

## 4. Criterio de pronto do F1 (cinco provas)

1. **Um merge na \`main\` deploya sozinho** — deploy na unha por SSH nao conta.
2. \`curl\` no \`/health/ready\` **pela VPS**.
3. A serie do \`job=$NOME\` visivel no Grafana Cloud.
4. O dashboard deste servico, com dados.
5. **Um alerta seu disparando de proposito e chegando no Telegram** — a unica que
   prova a corrente inteira.

E depois de todo merge, confira o run de \`push\`: **verde no CI nao e deployado**.
No hub um PR ficou 12 dias fora do ar porque um teste instavel pulou o job de deploy.
MARCADOR

# --- verificacao: o script confere o proprio resultado ---------------------------
echo "== verificacao =="
falhou=0

esperados=("PADROES.md" "LEIA-ME-KIT.md" "CLAUDE.md" "COMECE-AQUI.md"
           ".github/workflows/ci.yml" "docker-compose.yml" "docker-compose.prod.yml"
           "Dockerfile" "scripts/coverage-gate.py"
           "infra/postgres/provision-remote.sh" "infra/postgres/sql/$NOME-role.sql")
for f in "${esperados[@]}"; do
  [ -f "$DESTINO/$f" ] || { echo "  FALTA: $f"; falhou=1; }
done

# PADROES.md e LEIA-ME-KIT.md tem que sair IDENTICOS a origem (sao compartilhados)
for f in PADROES.md LEIA-ME-KIT.md; do
  if ! diff -q "$ORIGEM/$f" "$DESTINO/$f" >/dev/null; then
    echo "  ERRO: '$f' saiu diferente da origem — a substituicao mordeu um arquivo do kit"
    falhou=1
  fi
done

# resquicio de 'hub' fora dos arquivos do kit e do COMECE-AQUI (que cita o hub de proposito)
resto=$(grep -rilE "hub[-_]?precos|\bHub\." "$DESTINO" \
        --exclude=PADROES.md --exclude=LEIA-ME-KIT.md --exclude=COMECE-AQUI.md 2>/dev/null || true)
if [ -n "$resto" ]; then
  echo "  AVISO: ainda ha referencia ao hub nestes arquivos (confira a mao):"
  echo "$resto" | sed 's|^|    |'
fi

# o broker nao pode ir para producao neste repo — avisa alto se ainda estiver la
if grep -qE "^  plataforma-rabbitmq:" "$DESTINO/docker-compose.prod.yml" 2>/dev/null; then
  echo "  ATENCAO: docker-compose.prod.yml ainda define o servico 'plataforma-rabbitmq'."
  echo "           O broker e da plataforma e ja esta de pe — REMOVA antes do primeiro"
  echo "           deploy, ou voce sobe um segundo com o mesmo alias (secao 10.1)."
  echo "           O banner no topo do arquivo repete isso."
fi

# as guardas da secao 10 tem que ter sobrevivido a copia
for guarda in "colisão de alias" "whoami" "tr -d"; do
  grep -q "$guarda" "$DESTINO/.github/workflows/ci.yml" \
    || { echo "  ERRO: guarda '$guarda' sumiu do ci.yml"; falhou=1; }
done

if [ "$falhou" -ne 0 ]; then
  echo
  echo "VERIFICACAO REPROVOU. Confira '$DESTINO' antes de usar." >&2
  exit 1
fi

echo "  ok: arquivos presentes, kit intacto, guardas da secao 10 preservadas"
echo
echo "== pronto =="
echo "  Proximo passo: abra $DESTINO/COMECE-AQUI.md"
