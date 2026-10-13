import type { QueryClient } from "@tanstack/react-query";
import { advanceStatus, STATUS_RANK } from "./status";
import type { Order, OrderDetails, OrderStatusChangedEvent, Paged } from "./types";

export const queryKeys = {
  all: ["orders"] as const,
  lists: () => ["orders", "list"] as const,
  list: (status: string, page: number, pageSize: number) => ["orders", "list", status, page, pageSize] as const,
  detail: (id: string) => ["orders", "detail", id] as const,
};

/** Encontra um pedido já carregado em qualquer lista ou detalhe do cache. */
export function findCachedOrder(queryClient: QueryClient, orderId: string): Order | undefined {
  const detail = queryClient.getQueryData<OrderDetails>(queryKeys.detail(orderId));
  if (detail) return detail;

  for (const [, data] of queryClient.getQueriesData<Paged<Order>>({ queryKey: queryKeys.lists() })) {
    const found = data?.items.find((o) => o.id === orderId);
    if (found) return found;
  }
  return undefined;
}

/**
 * Aplica um evento de mudança de status no cache. É idempotente e tolerante a eventos fora de ordem:
 * o status nunca regride (Finalizado não volta para Processando) e o histórico não duplica entradas.
 * Retorna true se o evento trouxe informação nova.
 */
export function applyStatusEvent(queryClient: QueryClient, event: OrderStatusChangedEvent): boolean {
  const cached = findCachedOrder(queryClient, event.order_id);
  const isNew = !cached || STATUS_RANK[event.status_novo] > STATUS_RANK[cached.status];

  queryClient.setQueriesData<Paged<Order>>({ queryKey: queryKeys.lists() }, (old) =>
    old
      ? {
          ...old,
          items: old.items.map((o) =>
            o.id === event.order_id
              ? { ...o, status: advanceStatus(o.status, event.status_novo), data_atualizacao: event.alterado_em }
              : o,
          ),
        }
      : old,
  );

  queryClient.setQueryData<OrderDetails>(queryKeys.detail(event.order_id), (old) => {
    if (!old) return old;
    const alreadyRecorded = old.historico.some((h) => h.status_novo === event.status_novo);
    return {
      ...old,
      status: advanceStatus(old.status, event.status_novo),
      data_atualizacao: event.alterado_em,
      processamento_iniciado_em:
        event.status_novo === "Processando" ? (old.processamento_iniciado_em ?? event.alterado_em) : old.processamento_iniciado_em,
      finalizado_em: event.status_novo === "Finalizado" ? (old.finalizado_em ?? event.alterado_em) : old.finalizado_em,
      historico: alreadyRecorded
        ? old.historico
        : [
            ...old.historico,
            { status_anterior: event.status_anterior, status_novo: event.status_novo, alterado_em: event.alterado_em },
          ].sort((a, b) => Date.parse(a.alterado_em) - Date.parse(b.alterado_em)),
    };
  });

  return isNew;
}
