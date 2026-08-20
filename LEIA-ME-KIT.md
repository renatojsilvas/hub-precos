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

Manutenção: quando um padrão evoluir no tesouro-direto-api, atualize PADROES.md
aqui (é 1 arquivo, copiado entre os repos) — o guardião passa a cobrar o novo.
