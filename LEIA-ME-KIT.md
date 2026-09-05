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
5. Primeira fase (F1): peça "ultracode: crie o esqueleto da solução seguindo o
   molde do repo de referência" e observe se o guardiao-padroes roda na entrega.
   **O F1 não termina quando compila — termina deployado na VPS, mandando métrica
   para o Grafana.** Ver "O que o F1 tem que alcançar", logo abaixo.
6. **Leia a seção 10 do PADROES.md ANTES de portar qualquer coisa.** As seções 1–9
   vêm do repo de referência; a 10 é o que ele NÃO tem — cada item nasceu de algo
   que quebrou de verdade ao construir o `hub` (colisão de alias DNS derrubando o
   vizinho em produção, credencial provisionada sem verificação, arquivo do molde
   perdido no porte). Ela foi escrita para o próximo repo não repetir. Os itens
   10.12–10.17 vieram da fase de mensageria e são de outra natureza — não são sobre
   porte, são sobre **operar numa máquina pequena e compartilhada**: runtime que não
   enxerga o cgroup, teto de CPU que só corta rajada, serviço novo estourando o
   orçamento do vizinho, cache sem prazo, e commit mergeado que nunca chegou a
   produção. Se o seu repo vai dividir VPS com os outros, esses seis são os que vão
   te pegar.
7. **Se você vai CONDUZIR os agents, leia também a última seção deste arquivo**
   ("Erros de orquestração"). Ela é sobre o que o condutor errou — não o executor —
   e cada item passou por suíte verde antes de alguém notar.

Manutenção: quando um padrão evoluir no tesouro-direto-api, atualize PADROES.md
aqui (é 1 arquivo, copiado entre os repos) — o guardião passa a cobrar o novo.

---

# O que o F1 tem que alcançar

**O objetivo da primeira fase não é "esqueleto que compila". É esqueleto que está
rodando na VPS e cuja métrica aparece no Grafana** — sem um endpoint de negócio
sequer. Andar antes de correr: o caminho inteiro de entrega funcionando, com nada
dentro.

O `hub` fez ao contrário — esqueleto local primeiro, deploy muito depois — e pagou
caro por isso. Quando o deploy finalmente aconteceu, já havia código de verdade em
cima, e cada problema de infraestrutura chegou junto: colisão de alias DNS derrubando
o vizinho **em produção**, credencial provisionada sem verificação, segredo com quebra
de linha invisível, o scrape do alloy que ninguém tinha configurado. Com um esqueleto
vazio, todos esses seriam de resolução trivial e sem consequência. **Não repita a
ordem.**

E há um ganho que só existe nessa ordem: quando a primeira funcionalidade real
aparecer, o painel já está de pé para mostrá-la. Observabilidade montada depois é
observabilidade que ninguém confere.

## No seu repo

- `Dockerfile` multi-stage e **dois** composes: o local (com o serviço `app`, não só o
  banco — ver armadilha 2) e o de produção.
- `.github/workflows/ci.yml` com job de teste **e** job de deploy por SSH. Copie o do
  molde inteiro; o `hub` esqueceu o CI no porte e só notou muito depois (§10.10).
- Nome de serviço **único na rede** que você vai usar, conferido contra os aliases já
  registrados lá — não só único no seu arquivo (§10.1).
- `health`/`metrics` expostos e `Loki__Uri=http://alloy:3100`. Log é *push*: quem
  inicia é a sua aplicação, então ela precisa do endereço do destino.
- **Limites de recurso desde o primeiro deploy.** A VPS é pequena e compartilhada;
  serviço sem teto é serviço que derruba vizinho. Leia 10.12–10.14 do `PADROES` antes
  de escolher os números, e saiba quantos núcleos a máquina tem (`nproc`).
- `.env.example` documentando cada segredo: papel, default e o que quebra sem ele.

## No GitHub

Os secrets do deploy (`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`) e os do seu serviço.
Cadastre **antes** do primeiro merge: o deploy foi escrito para falhar cedo e com
mensagem clara se faltarem — mas falha.

## No repo do `tesouro-direto` (métrica é *pull*, mora lá)

Quem inicia a conexão precisa do endereço do outro — por isso log resolve no seu repo
e métrica não. São quatro edições, e a terceira é a que todo mundo esquece:

1. alvo do scrape em `infra/alloy/config.alloy`, com `job=<seu-servico>`;
2. dashboard em `infra/grafana/dashboards/`;
3. **o nome do dashboard na lista fixa do `apply-cloud.sh`** — copiar o JSON não basta;
4. regras de alerta como `rules-<seu-repo>.yaml`, **nunca** `rules.yaml`: esse nome já
   é das 21 regras do TD, e o PUT do publicador as sobrescreveria.

Depois rode o `apply-cloud.sh` com `GC_GRAFANA_URL`, `GC_GRAFANA_TOKEN` e
`TELEGRAM_BOT_TOKEN` **exportados na invocação** — o script não lê o `.env`, e a guarda
`${VAR:?}` só testa vazio: um placeholder como `not-configured-local-dev` passa por ela
e deixa o Telegram mudo, para todos os serviços, com o script reportando sucesso.

## Critério de pronto do F1

Quatro provas, nesta ordem — nenhuma delas é "o CI ficou verde":

1. `curl` no `/health/ready` **pela VPS**, respondendo.
2. A série do seu `job=` visível no Grafana Cloud.
3. O dashboard do seu serviço aparecendo lá, com dados.
4. **Um alerta seu disparando de propósito e chegando no Telegram.** Esta é a única que
   prova a corrente inteira. No `hub` os alertas ficaram semanas sem existir na nuvem
   porque os arquivos nunca tinham sido copiados para o outro repo, e o publicador
   pulava o bloco em silêncio, avisando "ausente — pulando" no meio de uma saída longa.

Só depois disso escreva a primeira migration.

---

# Armadilhas que custaram tempo no `hub` — leia antes de subir qualquer coisa

Oito coisas que quebraram de verdade ao levar o primeiro repo deste kit até a VPS.
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

**8. A ferramenta de teste é ponto único de falha do CI inteiro.**
O Testcontainers puxa o **Ryuk** do Docker Hub uma vez por execução, para faxinar
containers órfãos ao final. Num runner efêmero ele não tem função — a VM é destruída
junto com o job — mas tem custo: quando o Docker Hub devolveu HTTP 500 servindo o
manifesto dele, **163 dos 208 testes quebraram juntos em 9,8s**, inclusive os que nada
tinham a ver com a mudança. Desligue com `TESTCONTAINERS_RYUK_DISABLED=true` no CI. E
saiba reconhecer o padrão: suíte inteira falhando de uma vez, rápido demais para ter
executado, é infraestrutura comum — não é o seu código.

---

# Erros de orquestração no `hub` — o que o CONDUTOR errou, não o executor

A seção acima é sobre armadilhas do código. Esta é sobre erros de quem coordena os
agents. Todos aconteceram de verdade, ao construir o `hub`, e todos passaram pela
suíte verde — foram achados por revisão adversarial, por auditoria de conformidade, ou
pelo dono lendo o código. Se você vai orquestrar `operacoes` ou `custodia`, é aqui que
você vai errar.

## Especificar porta lendo a implementação, não a interface

O erro-mãe, e o que gerou três dos seguintes. Ao escrever a assinatura de uma porta no
prompt do executor, eu abria o `*ReadRepository.cs` do molde — a implementação — e
copiava a forma de lá. A implementação mostra o SQL; **só o arquivo `I*.cs` mostra o
contrato.**

Consequências concretas:

- **Portas de leitura sem `Result<T>`.** No molde é uniforme: seis métodos entre
  `ITituloReadRepository` e `IPrecoTaxaReadRepository`, todos `Task<Result<...>>`.
  Escrevi as do `hub` com tipo cru. Porta de leitura com tipo cru não reporta falha sem
  exceção, e força o chamador a engolir o erro ou usar try/catch de fluxo.
- **Primitivo onde existe value object.** As portas de escrita do próprio `hub` já
  usavam VO; as de leitura recebiam `string fonte, string classe`. Incoerência dentro
  do mesmo repo.
- **Parâmetros demais, e transponíveis.** `ObterPaginaDoCatalogoAsync(string? classe,
  string? busca, int skip, int take, ct)` — **dois pares adjacentes do mesmo tipo**. Um
  chamador passando `(busca, classe, take, skip)` compila e está errado, em silêncio.
  O molde tem no máximo 3 parâmetros antes do `CancellationToken`, em 28 métodos. Eu
  não conferi essa distribuição antes de escrever a minha.

**Regra:** antes de escrever qualquer assinatura num prompt, abra o `I*.cs` equivalente
no molde e copie a forma de lá. Conte os parâmetros. Se dois adjacentes têm o mesmo
tipo, encapsule ou reordene.

## Mandar o executor violar a camada

Três vezes eu especifiquei algo que quebrava a arquitetura, e o executor teve que me
corrigir (ou o revisor pegou depois):

- **`try/catch` na Application.** O molde tem **zero** `catch` em Domain e Application —
  verifique com `grep -rn "catch" src/*.Domain src/*.Application` antes de assumir.
  Todo tratamento vive na Infrastructure, onde a exceção nasce.
- **Capturar violação de índice único dentro do handler.** Faria a Application conhecer
  `DbUpdateException`/`PostgresException`. O molde põe isso no repositório
  (`UsuarioWriteRepository.AddOrGetExistingAsync`). O executor achou o molde certo e me
  corrigiu.
- **`try/catch` no read repository, quando o dono cobrou o `Result`.** Pareceu certo:
  dar caminho de falha real ao `Result`. Só que o `Error` herdava
  `ErrorType.Validation`, que mapeia para **HTTP 400** — então falha de banco virava
  "Requisição inválida" com `ex.Message` cru no `detail`. Era inofensivo enquanto só o
  job consumia; virou vazamento ao encostar num endpoint.

**Regra:** falha de infraestrutura é 500 pelo handler global, não `Result` de 400.
`Result` é para falha **esperada** de regra de negócio.

## Normalizar de um lado só

Mandei aplicar `Trim()` na guarda de boot da API key e não no middleware. Resultado: um
espaço acidental no fim da chave fazia a guarda **aprovar o boot** enquanto o middleware
rejeitava **todo cliente legítimo** — com 401 indistinguível de "chave errada", sem
pista nenhuma da causa. É a §10.4 do `PADROES` acontecendo por mão própria.

**Regra:** segredo que alimenta dois caminhos, normalize **uma vez na origem** e faça os
dois consumirem o mesmo valor. E aplique também no ponto de consumo, porque `Trim()` é
idempotente e blinda contra reordenamento futuro.

## Escrever contrato assumindo que os campos andam juntos

Especifiquei o `asof` como "a última data com preço e, dessa data, todos os campos".
Errado: o adapter pula campo nulo, então basta um dia sem `taxa_compra` para os campos
dessincronizarem — e aí o `pu_venda` **sumia da resposta** quando outro campo era mais
recente, sem `motivo`, sem log. O forward-fill tem que ser **por campo**.

**Regra:** ao desenhar contrato sobre dados esparsos, pergunte o que acontece quando
uma dimensão existe e a outra não.

## Endossar decisão antes de ter a resposta

Endossei trocar `Equals` por `Contains` numa lista de bloqueio — ganho real contra
placeholder com padding. Só que `Contains` é literal, e `CHANGE_ME_IN_PRODUCTION`
(underscore em vez de hífen) passava liso. Eu tinha **perguntado** sobre falso negativo
ao revisor e endossei antes da resposta chegar.

**Regra:** se você formulou a pergunta é porque desconfia. Espere a resposta.

## Erros de processo com os agents

- **Dois executores em paralelo no mesmo working tree** apagaram arquivos de teste um do
  outro. Delimitar "seus arquivos são estes" no prompt não impede: `Write` sobrescreve o
  arquivo inteiro. E mesmo com arquivos disjuntos, dois `dotnet build` concorrentes
  colidem em `obj/`/`bin/`.
- **O `revisor` NÃO é só-leitura.** Ele muta a implementação de propósito, para provar
  que um teste é vácuo, e reverte. Rodando junto com o `guardiao-padroes`, o guardião leu
  o estado mutado e reportou como defeito real um teste temporário que já não existia.
- **`isolation: "worktree"` não serve para revisar branch.** O worktree nasce da **base**
  (`main`), não do branch em que você está, e commitar antes não resolve. Tentei duas
  vezes; nas duas o revisor concluiu "a entrega não existe" e recomendou devolver ao
  executor — conclusão que, aceita sem conferir, mandaria refazer trabalho pronto.

**Regra:** `Explore` e `guardiao-padroes` são só-leitura e vão em paralelo com qualquer
coisa. O `revisor` roda **sozinho**. Executores que escrevem, um de cada vez.

## Não gravar o desvio que você mesmo aprovou

Aceitei dois desvios do molde (um record de parâmetros que o molde não tem; um índice
que decidi não criar) e não gravei nenhum dos dois. O `guardiao-padroes` cobrou os dois,
com razão: o `CLAUDE.md` exige registro, e desvio não registrado vira precedente
esquecido — o próximo repo copia sem saber que era exceção.

## Deixar o escopo vir da doc em vez do arquivo

Quase entreguei uma etapa pela metade. A fila de tarefas existia só no contexto de uma
sessão e nunca tinha sido commitada; eu me guiei pela ordem de implementação do plano
de arquitetura, que nomeava só um dos dois endpoints da etapa. **Commite o roadmap.**
A próxima sessão lê o escopo do repo, não reconstrói do contexto.

## Deixar o deploy para depois de existir código

Defini o F1 do `hub` como "esqueleto, sem endpoints de negócio" e parei aí — compilava
e subia na minha máquina. O deploy veio fases depois.

Resultado: toda a dor de infraestrutura chegou de uma vez, com código de verdade já em
cima. A colisão de alias DNS **derrubou o vizinho em produção**; o segredo com quebra
de linha fez a role nascer com uma senha e a aplicação enviar outra; o scrape do alloy
não existia, então não havia métrica nenhuma para olhar enquanto se depurava. Com um
esqueleto vazio, cada um desses seria um susto de dez minutos sem consequência.

O custo real não é o tempo: é que problema de deploy e problema de código chegam
misturados, e você não sabe qual está olhando.

**Regra:** o F1 termina **deployado e observável**, não "compilando". Ver "O que o F1
tem que alcançar" na primeira seção deste arquivo — o critério de pronto tem quatro
provas, e a última é um alerta seu chegando no Telegram.

## Dimensionar recurso sem medir, e chamar o número de "folgado"

O advisor recomendou **peso** de CPU (`cpu_shares`), com o argumento de que CPU não se
estoca. Eu mantive o peso e acrescentei um teto rígido de `0.60`, escrevendo no
comentário que era "folgado, só para conter loop desgovernado". O número não veio de
lugar nenhum — nem de medição, nem do molde, nem do advisor.

Duas semanas depois, alerta de throttling. As rajadas do broker batiam em **59,99%**:
cortadas exatamente no teto que eu inventei. E o teto não protegia nada, porque a VPS
tem **um núcleo** — o processo já não podia passar de 1.0.

**Regra:** número de limite ou é medido, ou vem do molde, ou é decisão registrada com o
motivo. "Parece folgado" não é nenhuma das três. E antes de dimensionar qualquer coisa,
saiba quantos núcleos a máquina tem — `nproc` muda o significado de todo teto de CPU.

## Remover uma garantia implícita sem procurar quem dependia dela

Troquei `depends_on: service_healthy` por `service_started` no broker, por um motivo
correto: um broker doente não pode impedir a API de **leitura** de subir. A mudança
estava certa. O que eu não fiz foi perguntar **quem estava se apoiando naquela espera**.

Estava: a verificação de credencial do deploy rodava logo depois do `up -d` e só
funcionava porque o `service_healthy` segurava o retorno. Sem ele, o `curl` bateu numa
porta fechada 6 segundos depois e o deploy reprovou dizendo "credencial diverge" —
diagnóstico errado, para um problema de tempo.

**Regra:** ao remover uma espera, um lock, um `depends_on` ou qualquer ordenação, faça
`grep` por quem vem depois no mesmo fluxo. Garantia implícita não aparece em teste
unitário e não deixa rastro no diff.

## Reintroduzir, por outra porta, o acoplamento que você acabou de evitar

Discuti com cuidado por que o relay **não** entra no `/health/ready`: o readiness
alimenta o healthcheck do container, e broker fora do ar não pode degradar a API de
leitura, que não depende dele. Provei empiricamente. Escrevi no PR.

E aí pus `depends_on: plataforma-rabbitmq: condition: service_healthy` no mesmo serviço
— que faz um broker doente impedir o Hub de subir. É o mesmo acoplamento, com outro
nome, três dezenas de linhas abaixo no mesmo arquivo.

**Regra:** depois de decidir "X não pode depender de Y", procure no arquivo inteiro
todas as formas de X depender de Y. Health check, `depends_on`, ordem de boot, timeout
compartilhado, pool compartilhada.

## Especular em vez de medir — e repetir isso três vezes

Na investigação do throttling levantei três hipóteses e escrevi cada uma com confiança:
pressão de memória do host, busy-wait dos schedulers do Erlang, e o relay abrindo canal
a cada 5s. **As três estavam erradas**, e cada uma custou uma ida e volta com o dono.

O que resolveu foram três medições: `free -m` (descartou memória), `scheduler_wall_time`
(0,19% — a VM estava ociosa, matando o busy-wait) e um laço de `docker stats` que
revelou o **formato** da curva — rajadas paradas exatamente na cota. O formato disse o
que nenhum raciocínio meu tinha dito: era trabalho cortado, não trabalho excessivo.

**Regra:** em diagnóstico de recurso, a primeira ação é medir, não hipotetizar. E
quando a primeira hipótese cai, isso é sinal para medir mais, não para hipotetizar de
novo. Se você já errou duas vezes, pare de propor causa e peça dado.

## Escolher a prova difícil quando existe uma fácil

Pedi a um executor que provasse empiricamente a diferença entre dois healthchecks do
RabbitMQ — medir com timestamps que um fica verde antes de a porta abrir e o outro não.
Ele empacou horas num problema de permissão do próprio arranjo de teste.

A prova era desnecessária. O deploy não precisa saber **quando** o healthcheck fica
verde: ele precisa esperar pela **condição que ele mesmo usa** — a API respondendo. Um
laço em volta da própria verificação se autovalida e dispensa medir qualquer coisa. Ao
matar o agente e fazer eu mesmo, o diff saiu em minutos.

**Regra:** antes de mandar alguém provar uma propriedade do sistema, pergunte se dá
para escrever o código de um jeito que não dependa daquela propriedade. E se um agente
está há muito tempo no mesmo ponto, olhe o que ele está fazendo — o problema pode não
ser a tarefa.

## Aceitar diagnóstico de terceiro sem conferir contra o código

Chegou uma análise externa do teste que estava falhando. Ela acertou **qual** teste era
e errou o resto: propunha aceitar também um código de erro `TdApi.Timeout` que **não
existe no projeto** (`AdapterErrors` tem quatro, e esse não é um deles), sem citar o
valor real da falha — sinal de que não tinha lido o detalhe.

Aplicada, teria trocado um teste vermelho por um teste **cego**: ele passaria justamente
no cenário que existe para reprovar, o do timeout não funcionar.

**Regra:** diagnóstico de fora se confere contra o código antes de virar correção.
Comece pelo mais barato: os identificadores citados existem no repo?

## Registrar em todo lugar, menos no arquivo certo

Gravei as lições desta etapa em mensagem de commit, corpo de PR e no grafo da memória.
Nenhum desses lugares é lido por quem for criar o `custodia`. O `PADROES.md` §10 e esta
seção — os dois arquivos que existem exatamente para isso — ficaram intactos até o dono
perguntar onde estavam as lições.

É a §10.10 do `PADROES` (comparar por ausência) acontecendo com quem escreveu a §10.10.

**Regra:** ao fechar uma etapa, o checklist não é "eu registrei?" e sim "está no arquivo
que a próxima pessoa vai abrir?". Commit e PR são registro de **quando**; `PADROES.md` e
este arquivo são registro de **o que não repetir**.

## Dois vícios de relato

- **Atribuir ao dono uma decisão que foi inferência sua.** Escrevi "escolha sua" sobre
  algo que eu tinha deduzido da frase dele. Se você inferiu, diga que inferiu.
- **Somar errado e afirmar com confiança.** Reportei uma contagem de testes 10 acima da
  real, com a saída do comando colada logo acima. Confira o número que você acabou de
  escrever contra a saída que você acabou de colar.
- **Ler evidência com viés de confirmação.** Querendo confirmar que um container tinha
  reiniciado, afirmei que "o `MachineName` do log mudou, logo reiniciou". Eu não tinha
  valor anterior para comparar — e `docker restart` preserva o container, então esse
  campo nem mudaria. Inventei uma prova para a conclusão que eu já queria. Antes de
  chamar algo de evidência, pergunte o que ela valeria se a sua hipótese fosse falsa.
