export type OrderStatus = "Pendente" | "Processando" | "Finalizado";

export interface Order {
  id: string;
  cliente: string;
  produto: string;
  valor: number;
  status: OrderStatus;
  data_criacao: string;
  data_atualizacao: string;
  processamento_iniciado_em: string | null;
  finalizado_em: string | null;
}

export interface StatusHistoryEntry {
  status_anterior: OrderStatus | null;
  status_novo: OrderStatus;
  alterado_em: string;
}

export interface OrderDetails extends Order {
  historico: StatusHistoryEntry[];
}

export interface Paged<T> {
  items: T[];
  page: number;
  page_size: number;
  total: number;
}

export interface CreateOrderInput {
  cliente: string;
  produto: string;
  valor: number;
}

/** Evento enviado pelo SignalR a cada mudanca de status. */
export interface OrderStatusChangedEvent {
  order_id: string;
  status_anterior: OrderStatus | null;
  status_novo: OrderStatus;
  alterado_em: string;
}

export interface ExecutedQuery {
  ferramenta: string;
  parametros: string;
  sql: string | null;
}

export interface AskResponse {
  resposta: string;
  consultas_executadas: ExecutedQuery[];
}

export interface AiStatus {
  habilitado: boolean;
  modelo: string | null;
}
