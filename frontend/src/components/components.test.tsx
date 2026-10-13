import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Order } from "@/lib/types";
import { OrderStepper } from "./order-stepper";
import { OrderTimeline } from "./order-timeline";
import { OrdersTable } from "./orders-table";
import { RealtimeContext, useRefetchInterval, type ConnectionStatus } from "./realtime-provider";
import { RealtimeIndicator } from "./realtime-indicator";
import { StatusBadge } from "./status-badge";

function withConnection(connection: ConnectionStatus, ui: React.ReactNode, flashing: string[] = []) {
  return <RealtimeContext.Provider value={{ connection, flashing: new Set(flashing) }}>{ui}</RealtimeContext.Provider>;
}

describe("StatusBadge", () => {
  it.each(["Pendente", "Processando", "Finalizado"] as const)("mostra o status %s", (status) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByTestId("status-badge")).toHaveTextContent(status);
    expect(screen.getByTestId("status-badge")).toHaveAttribute("data-status", status);
  });
});

describe("OrderStepper", () => {
  const states = (status: "Pendente" | "Processando" | "Finalizado") => {
    render(<OrderStepper status={status} />);
    return Object.fromEntries(
      ["Pendente", "Processando", "Finalizado"].map((s) => [s, document.querySelector(`[data-step="${s}"]`)?.getAttribute("data-state")]),
    );
  };

  it("Pendente: primeira etapa atual", () => {
    expect(states("Pendente")).toEqual({ Pendente: "current", Processando: "todo", Finalizado: "todo" });
  });

  it("Processando: primeira concluída, segunda atual", () => {
    expect(states("Processando")).toEqual({ Pendente: "done", Processando: "current", Finalizado: "todo" });
  });

  it("Finalizado: todas concluídas", () => {
    expect(states("Finalizado")).toEqual({ Pendente: "done", Processando: "done", Finalizado: "done" });
  });
});

describe("OrderTimeline", () => {
  it("lista o histórico em ordem com o tempo entre as etapas", () => {
    render(
      <OrderTimeline
        history={[
          { status_anterior: null, status_novo: "Pendente", alterado_em: "2026-10-09T12:00:00Z" },
          { status_anterior: "Pendente", status_novo: "Processando", alterado_em: "2026-10-09T12:00:00.500Z" },
          { status_anterior: "Processando", status_novo: "Finalizado", alterado_em: "2026-10-09T12:00:05.500Z" },
        ]}
      />,
    );

    const items = screen.getByTestId("order-timeline").querySelectorAll("li");
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent("Pedido criado");
    expect(items[1]).toHaveTextContent("+500 ms");
    expect(items[2]).toHaveTextContent("+5,0 s");
  });
});

describe("OrdersTable", () => {
  const orders: Order[] = [
    { id: "a1", cliente: "Ana", produto: "Notebook", valor: 100, status: "Pendente", data_criacao: "2026-10-09T12:00:00Z", data_atualizacao: "2026-10-09T12:00:00Z", processamento_iniciado_em: null, finalizado_em: null },
    { id: "b2", cliente: "Bruno", produto: "Mouse", valor: 50, status: "Finalizado", data_criacao: "2026-10-09T12:01:00Z", data_atualizacao: "2026-10-09T12:01:06Z", processamento_iniciado_em: null, finalizado_em: null },
  ];

  it("renderiza tabela (desktop) e cards (celular) com os mesmos pedidos", () => {
    render(withConnection("connected", <OrdersTable orders={orders} />));

    expect(screen.getByTestId("orders-table").querySelectorAll("tbody tr")).toHaveLength(2);
    expect(screen.getByTestId("orders-cards").querySelectorAll("li")).toHaveLength(2);
    expect(screen.getAllByText("Ana")).toHaveLength(2);
  });

  it("destaca a linha cujo status acabou de mudar", () => {
    render(withConnection("connected", <OrdersTable orders={orders} />, ["b2"]));

    const flashed = document.querySelectorAll(".animate-row-flash");
    expect(flashed.length).toBeGreaterThan(0);
    flashed.forEach((el) => expect(el.closest("[data-order-id]")?.getAttribute("data-order-id") ?? el.getAttribute("data-order-id")).toBe("b2"));
  });
});

describe("tempo real com fallback", () => {
  const cases: [ConnectionStatus, string][] = [
    ["connected", "Tempo real"],
    ["connecting", "Conectando…"],
    ["reconnecting", "Reconectando…"],
    ["offline", "Atualização periódica"],
  ];

  it.each(cases)("indicador %s mostra %s", (connection, label) => {
    render(withConnection(connection, <RealtimeIndicator />));
    expect(screen.getByTestId("realtime-indicator")).toHaveTextContent(label);
  });

  function Interval() {
    return <span data-testid="interval">{useRefetchInterval()}</span>;
  }

  it("polling lento com tempo real ativo e rápido (3s) quando a conexão cai", () => {
    const { unmount } = render(withConnection("connected", <Interval />));
    expect(screen.getByTestId("interval")).toHaveTextContent("30000");
    unmount();

    for (const connection of ["offline", "reconnecting", "connecting"] as const) {
      const view = render(withConnection(connection, <Interval />));
      expect(screen.getByTestId("interval")).toHaveTextContent("3000");
      view.unmount();
    }
  });
});

describe("animação do ícone", () => {
  const spinning = () => document.querySelector("svg.animate-spin") !== null;

  it("Processando atual gira; Processando já superado no histórico não gira", () => {
    const { unmount } = render(<StatusBadge status="Processando" />);
    expect(spinning()).toBe(true);
    unmount();

    render(
      <OrderTimeline
        history={[
          { status_anterior: null, status_novo: "Pendente", alterado_em: "2026-10-09T12:00:00Z" },
          { status_anterior: "Pendente", status_novo: "Processando", alterado_em: "2026-10-09T12:00:01Z" },
          { status_anterior: "Processando", status_novo: "Finalizado", alterado_em: "2026-10-09T12:00:06Z" },
        ]}
      />,
    );
    expect(spinning()).toBe(false);
  });

  it("se o pedido ainda está Processando, o último item do histórico gira", () => {
    render(
      <OrderTimeline
        history={[
          { status_anterior: null, status_novo: "Pendente", alterado_em: "2026-10-09T12:00:00Z" },
          { status_anterior: "Pendente", status_novo: "Processando", alterado_em: "2026-10-09T12:00:01Z" },
        ]}
      />,
    );
    expect(spinning()).toBe(true);
  });
});

describe("Markdown da IA", () => {
  it("renderiza negrito e tabela em vez de mostrar os símbolos", async () => {
    const { Markdown } = await import("./markdown");
    const { container } = render(
      <Markdown>{"O cliente **Ana** pagou:\n\n| Cliente | Valor |\n|---|---|\n| Ana | R$ 10,00 |"}</Markdown>,
    );

    expect(container.querySelector("strong")).toHaveTextContent("Ana");
    expect(container.querySelectorAll("table th")).toHaveLength(2);
    expect(container.querySelector("td")).toHaveTextContent("Ana");
    expect(container.textContent).not.toContain("**");
    expect(container.textContent).not.toContain("|---|");
  });

  it("não interpreta HTML bruto vindo dos dados (proteção contra injeção)", async () => {
    const { Markdown } = await import("./markdown");
    const { container } = render(<Markdown>{'Cliente <img src=x onerror="alert(1)"> <script>alert(1)</script>'}</Markdown>);

    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector("script")).toBeNull();
  });
});

describe("Markdown da IA: imagens", () => {
  it("não renderiza imagens (evita exfiltração por URL via prompt injection)", async () => {
    const { Markdown } = await import("./markdown");
    const { container } = render(<Markdown>{"texto ![x](https://exemplo.invalido/?d=segredo) fim"}</Markdown>);

    expect(container.querySelector("img")).toBeNull();
    expect(container.textContent).toContain("texto");
  });
});
