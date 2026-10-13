import type { OrderStatus } from "./types";

/** Ordem do ciclo de vida: usada para ignorar eventos atrasados/fora de ordem. */
export const STATUS_RANK: Record<OrderStatus, number> = {
  Pendente: 1,
  Processando: 2,
  Finalizado: 3,
};

export const STATUS_FLOW: OrderStatus[] = ["Pendente", "Processando", "Finalizado"];

export const STATUS_DESCRIPTION: Record<OrderStatus, string> = {
  Pendente: "Pedido recebido, aguardando processamento",
  Processando: "Pedido sendo processado pelo worker",
  Finalizado: "Pedido concluído",
};

export function isOrderStatus(value: unknown): value is OrderStatus {
  return value === "Pendente" || value === "Processando" || value === "Finalizado";
}

/** Retorna o status mais avançado: nunca regride Finalizado -> Processando por causa de um evento atrasado. */
export function advanceStatus(current: OrderStatus, incoming: OrderStatus): OrderStatus {
  return STATUS_RANK[incoming] > STATUS_RANK[current] ? incoming : current;
}
