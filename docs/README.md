# Documentação de arquitetura

Este repo não duplica arquitetura — é só um ponteiro. A fonte canônica é
[`plataforma-docs/ARQUITETURA.md`](../../plataforma-docs/ARQUITETURA.md), seção 10:
as 12 ADRs do plano de arquitetura da plataforma (hub, operações, custódia).

As mesmas 12 ADRs também vivem no MCP `memoria`, como entidades `entityType: ADR`
(nomeadas `ADR-N · <título>`, com as relações entre elas) — útil para consulta rápida
em sessão de agente, mas nada sincroniza automaticamente entre os dois lugares. Se o
grafo da memória divergir da seção 10 (vazio, ou com ADRs faltando), a seção 10 é
quem vale; recarregue a memória a partir dela.
