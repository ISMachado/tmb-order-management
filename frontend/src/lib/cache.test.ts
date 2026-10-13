import { QueryClient } from "@tanstack/react-query";
import { beforeEach, describe, expect, it } from "vitest";
import { applyStatusEvent, queryKeys } from "./cache";
import type { Order, OrderDetails, OrderStatusChangedEvent, Paged } from "./types";

const ID = "01a12147-bc07-7fd3-b1cb-32272237a043";

const order = (status: Order["status"], id = ID): Order => ({
  id,
  cliente: "Ana",
  produto: "Notebook",
  valor: 10,
  status,
  data_criacao: "2026-10-09T12:00:00Z",
  data_atualizacao: "2026-10-09T12:00:00Z",
  processamento_iniciado_em: null,
  finalizado_em: null,
});

const details = (status: Order["status"]): OrderDetails => ({
  ...order(status),
  historico: [{ status_anterior: null, status_novo: "Pendente", alterado_em: "2026-10-09T12:00:00Z" }],
});

const event = (anterior: OrderStatusChangedEvent["status_anterior"], novo: OrderStatusChangedEvent["status_novo"], at: string): OrderStatusChangedEvent => ({
  order_id: ID,
  status_anterior: anterior,
  status_novo: novo,
  alterado_em: at,
});

describe("applyStatusEvent", () => {
  let client: QueryClient;
  const listKey = queryKeys.list("", 1, 10);

  beforeEach(() => {
    client = new QueryClient();
    const page: Paged<Order> = { items: [order("Pendente"), order("Pendente", "outro")], page: 1, page_size: 10, total: 2 };
    client.setQueryData(listKey, page);
    client.setQueryData(queryKeys.detail(ID), details("Pendente"));
  });

  it("atualiza o status na lista e no detalhe sem tocar nos outros pedidos", () => {
    const isNew = applyStatusEvent(client, event("Pendente", "Processando", "2026-10-09T12:00:01Z"));

    expect(isNew).toBe(true);
    const list = client.getQueryData<Paged<Order>>(listKey)!;
    expect(list.items.find((o) => o.id === ID)?.status).toBe("Processando");
    expect(list.items.find((o) => o.id === "outro")?.status).toBe("Pendente");
    const detail = client.getQueryData<OrderDetails>(queryKeys.detail(ID))!;
    expect(detail.status).toBe("Processando");
    expect(detail.processamento_iniciado_em).toBe("2026-10-09T12:00:01Z");
    expect(detail.historico.map((h) => h.status_novo)).toEqual(["Pendente", "Processando"]);
  });

  it("é idempotente: o mesmo evento duas vezes não duplica o histórico nem avisa de novo", () => {
    const e = event("Pendente", "Processando", "2026-10-09T12:00:01Z");
    expect(applyStatusEvent(client, e)).toBe(true);
    expect(applyStatusEvent(client, e)).toBe(false);

    expect(client.getQueryData<OrderDetails>(queryKeys.detail(ID))!.historico).toHaveLength(2);
  });

  it("ignora evento atrasado: Finalizado não volta para Processando", () => {
    applyStatusEvent(client, event("Pendente", "Processando", "2026-10-09T12:00:01Z"));
    applyStatusEvent(client, event("Processando", "Finalizado", "2026-10-09T12:00:06Z"));

    const isNew = applyStatusEvent(client, event("Pendente", "Processando", "2026-10-09T12:00:01Z"));

    expect(isNew).toBe(false);
    expect(client.getQueryData<OrderDetails>(queryKeys.detail(ID))!.status).toBe("Finalizado");
    expect(client.getQueryData<Paged<Order>>(listKey)!.items[0].status).toBe("Finalizado");
  });

  it("eventos fora de ordem produzem o mesmo histórico ordenado", () => {
    applyStatusEvent(client, event("Processando", "Finalizado", "2026-10-09T12:00:06Z"));
    applyStatusEvent(client, event("Pendente", "Processando", "2026-10-09T12:00:01Z"));

    const detail = client.getQueryData<OrderDetails>(queryKeys.detail(ID))!;
    expect(detail.historico.map((h) => h.status_novo)).toEqual(["Pendente", "Processando", "Finalizado"]);
    expect(detail.status).toBe("Finalizado");
  });

  it("não quebra quando o pedido não está em cache", () => {
    const other = new QueryClient();
    expect(applyStatusEvent(other, event("Pendente", "Processando", "2026-10-09T12:00:01Z"))).toBe(true);
  });
});
