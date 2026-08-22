# Kit de padrões — como instalar em cada repo novo (hub, operacoes, custodia)

1. Copie para a raiz do repo novo: `CLAUDE.md`, `PADROES.md`, `.claude/agents/`
   (5 agents: executor, tarefas-leves, advisor, revisor, guardiao-padroes).
2. Clone o repo de referência como irmão: `git clone <tesouro-direto> ../tesouro-direto-api`
   (somente leitura — jamais editar por aqui). O nome do diretório clonado precisa ser
   exatamente `tesouro-direto-api`, conforme declarado em `CLAUDE.md` — essa divergência
   impede os agents de localizar o molde.
3. Na sessão do Claude Code: `/add-dir ../tesouro-direto-api` (dá aos agents acesso
   de leitura ao código de referência). Confirme os agents com `/agents`.
4. Memória: `claude mcp add memoria -- npx -y @modelcontextprotocol/server-memory`
   e semeie com as ADRs 1–12 do plano de arquitetura (cole a seção 10 do
   `plano-hub-custodia.md` e peça para gravar como entidades/relações).
5. Teste de fumaça: peça "ultracode: crie o esqueleto da solução seguindo o
   molde do repo de referência" e observe se o guardiao-padroes roda na entrega.
6. **Leia a seção 10 do PADROES.md ANTES de portar qualquer coisa.** As seções 1–9
   vêm do repo de referência; a 10 é o que ele NÃO tem — cada item nasceu de algo
   que quebrou de verdade ao construir o `hub` (colisão de alias DNS derrubando o
   vizinho em produção, credencial provisionada sem verificação, arquivo do molde
   perdido no porte). Ela foi escrita para o próximo repo não repetir.

Manutenção: quando um padrão evoluir no tesouro-direto-api, atualize PADROES.md
aqui (é 1 arquivo, copiado entre os repos) — o guardião passa a cobrar o novo.

---

# Armadilhas que custaram tempo no `hub` — leia antes de subir qualquer coisa

Sete coisas que quebraram de verdade ao levar o primeiro repo deste kit até a VPS.
Nenhuma está no repo de referência: ou ele nunca enfrentou o caso, ou tem o mesmo
defeito e ninguém tinha esbarrado ainda. A pior derrubou um serviço em produção.

**1. Nome de serviço no compose vira alias DNS na rede.**
Ao entrar numa rede que já existe (`external: true`), um serviço chamado `app` colide
com o `app` que já está lá. O Docker não trata como erro: guarda os dois IPs sob o mesmo
nome e devolve a lista alternada. Metade das chamadas do vizinho cai em você, sem erro
nem log. Foi assim que o `tesouro-direto` passou a responder de forma intermitente, e
metade das raspagens de métrica dele vinha do Hub, poluindo a série de onde saem 12 das
18 regras de alerta. Já ocupados na `tesouro-net`: `app`, `db`, `web`, `alloy`,
`tesouro-direto-*`. O nome tem que ser único **na rede**, não só no seu arquivo.

**2. Ponha o serviço `app` no compose desde o começo.**
O molde tem. Se o seu `docker-compose.yml` só sobe o banco, a única forma documentada de
rodar vira `dotnet run` — justamente a que exige SDK instalado, user-secrets e
`launchSettings.json`. `docker compose up -d` e pronto é o que quem chega quer, e é o
mais parecido com produção.

**3. Sem `Properties/launchSettings.json`, `dotnet run` não roda.**
Sem ele a aplicação sobe em `Production`, user-secrets não são carregados, e o boot falha
por falta de credencial. O molde tem esse arquivo e é fácil não portar. Corolário: teste
com o comando **cru** do README, nunca com variável de ambiente na frente — foi assim que
essa falta passou por executores e por duas revisões adversariais, e só apareceu quando o
dono rodou o comando de verdade.

**4. Observabilidade se configura no repo do `tesouro-direto`, não no seu.**
São três edições lá: o alvo do scrape em `infra/alloy/config.alloy`, o dashboard em
`infra/grafana/dashboards/` **mais o nome dele na lista fixa do `apply-cloud.sh`** (copiar
o JSON não basta), e a regra de alerta em `infra/grafana/cloud/rules.yaml`.
Log é o oposto e resolve no seu repo: basta `Loki__Uri=http://alloy:3100`. A razão é de
protocolo — log é *push* (quem inicia é a aplicação, então ela precisa do endereço do
destino) e métrica é *pull* (quem inicia é o coletor, então ele precisa do endereço do
alvo). Quem inicia a conexão precisa do endereço do outro.

**5. Segredo que alimenta dois caminhos precisa ser normalizado na origem.**
`docker exec -e` preserva o valor byte a byte; o `.env` do compose é lido **por linha** e
descarta um `\n`/`\r` final. Um segredo colado com quebra de linha faz a role nascer com
uma senha e a aplicação enviar outra. O pior é o diagnóstico: comparar os dois lados a
partir do `.env` mostra que batem, porque a leitura já normalizou. Normalize com
`tr -d '\r\n'` uma vez, antes de qualquer uso.

**6. O SQL que cria a role não cria o database.**
Localmente quem cria é o `POSTGRES_DB` do compose, via entrypoint da imagem. Num cluster
que já está de pé não há entrypoint nenhum, e ninguém cria. Faça o `CREATE DATABASE` num
passo separado — ele não roda dentro de transação nem de bloco `DO`.

**7. Testar credencial por `127.0.0.1` sempre passa.**
O `pg_hba.conf` da imagem oficial do Postgres tem `host all all 127.0.0.1/32 trust`. Por
loopback, qualquer senha autentica — a verificação passaria sempre e seria teatro.
Verificação de credencial tem que sair por um container cliente na mesma rede docker. E
verifique: provisionar sem testar a credencial que você acabou de criar é afirmar sem
evidência, e o defeito reaparece minutos depois, longe da causa.
