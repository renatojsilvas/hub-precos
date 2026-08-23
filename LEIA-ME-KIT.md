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

## Dois vícios de relato

- **Atribuir ao dono uma decisão que foi inferência sua.** Escrevi "escolha sua" sobre
  algo que eu tinha deduzido da frase dele. Se você inferiu, diga que inferiu.
- **Somar errado e afirmar com confiança.** Reportei uma contagem de testes 10 acima da
  real, com a saída do comando colada logo acima. Confira o número que você acabou de
  escrever contra a saída que você acabou de colar.
