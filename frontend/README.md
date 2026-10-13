# Frontend — Gestão de Pedidos

Next.js 16 (App Router) + TypeScript + Tailwind CSS 4. Documentação completa do projeto no [README da raiz](../README.md).

## Telas

| Rota | O que faz |
|---|---|
| `/pedidos` | Lista responsiva (tabela no desktop, cards no celular), filtro por status, paginação e criação de pedido |
| `/pedidos/[id]` | Detalhes, progresso por etapas e histórico de status |
| `/perguntas` | Módulo de IA: perguntas em linguagem natural sobre os pedidos |

## Tempo real

`components/realtime-provider.tsx` mantém uma conexão SignalR com a API (`/hubs/orders`) e aplica cada evento `OrderStatusChanged` no cache do TanStack Query (`lib/cache.ts`), de forma idempotente e tolerante a eventos fora de ordem. Se a conexão cair, os dados passam a ser consultados a cada 3 s (fallback) até ela voltar.

## Desenvolvimento

```bash
npm ci
NEXT_PUBLIC_API_URL=http://localhost:5080 npm run dev   # http://localhost:3000
npm test          # Vitest + Testing Library
npm run lint && npm run typecheck
```

`NEXT_PUBLIC_API_URL` é lida pelo navegador e embutida no bundle em tempo de build (no Docker, via `build.args` do `docker-compose.yml`).
