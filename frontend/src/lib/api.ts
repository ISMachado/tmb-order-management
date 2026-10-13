import type {
  AiStatus,
  AskResponse,
  CreateOrderInput,
  Order,
  OrderDetails,
  OrderStatus,
  Paged,
} from "./types";

export const API_URL = (process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080").replace(/\/$/, "");

/** Erro da API no formato ProblemDetails (RFC 9457). */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: { "Content-Type": "application/json", ...init?.headers },
    });
  } catch {
    throw new ApiError("Não foi possível conectar à API. Verifique se o servidor está no ar.", 0);
  }

  if (!response.ok) {
    let title = `Erro ${response.status}`;
    let fieldErrors: Record<string, string[]> = {};
    try {
      const problem = await response.json();
      title = problem.detail ?? problem.title ?? title;
      fieldErrors = problem.errors ?? {};
    } catch {
      // Corpo vazio ou não-JSON: mantém a mensagem padrão.
    }
    throw new ApiError(title, response.status, fieldErrors);
  }

  return (await response.json()) as T;
}

export interface ListOrdersParams {
  status?: OrderStatus | "";
  page?: number;
  pageSize?: number;
}

export const api = {
  listOrders({ status, page = 1, pageSize = 10 }: ListOrdersParams = {}) {
    const query = new URLSearchParams({ page: String(page), page_size: String(pageSize) });
    if (status) query.set("status", status);
    return request<Paged<Order>>(`/orders?${query}`);
  },

  getOrder(id: string) {
    return request<OrderDetails>(`/orders/${id}`);
  },

  /** A Idempotency-Key evita pedidos duplicados em caso de clique duplo ou reenvio. */
  createOrder(input: CreateOrderInput, idempotencyKey: string) {
    return request<Order>("/orders", {
      method: "POST",
      headers: { "Idempotency-Key": idempotencyKey },
      body: JSON.stringify(input),
    });
  },

  aiStatus() {
    return request<AiStatus>("/ai/status");
  },

  ask(pergunta: string) {
    return request<AskResponse>("/ai/ask", {
      method: "POST",
      body: JSON.stringify({ pergunta }),
    });
  },
};
