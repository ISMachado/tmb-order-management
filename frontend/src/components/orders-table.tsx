"use client";

import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDateTime, shortId } from "@/lib/format";
import type { Order } from "@/lib/types";
import { useRealtime } from "./realtime-provider";
import { StatusBadge } from "./status-badge";

/**
 * Tabela responsiva: em telas médias+ vira uma tabela tradicional; no celular cada pedido vira um card.
 * A linha pisca quando o status muda em tempo real.
 */
export function OrdersTable({ orders }: { orders: Order[] }) {
  const { flashing } = useRealtime();
  const flashClass = (id: string) => (flashing.has(id) ? "animate-row-flash" : "");

  return (
    <>
      {/* Desktop / tablet */}
      <div className="hidden overflow-hidden rounded-xl border border-slate-200 bg-white md:block dark:border-slate-800 dark:bg-slate-900">
        <table className="w-full text-left text-sm" data-testid="orders-table">
          <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500 dark:bg-slate-800/60 dark:text-slate-400">
            <tr>
              <th scope="col" className="px-4 py-3 font-medium">Pedido</th>
              <th scope="col" className="px-4 py-3 font-medium">Cliente</th>
              <th scope="col" className="px-4 py-3 font-medium">Produto</th>
              <th scope="col" className="px-4 py-3 text-right font-medium">Valor</th>
              <th scope="col" className="px-4 py-3 font-medium">Status</th>
              <th scope="col" className="px-4 py-3 font-medium">Criado em</th>
              <th scope="col" className="px-4 py-3"><span className="sr-only">Detalhes</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
            {orders.map((order) => (
              <tr
                key={order.id}
                data-order-id={order.id}
                className={cn("transition-colors hover:bg-slate-50 dark:hover:bg-slate-800/50", flashClass(order.id))}
              >
                <td className="px-4 py-3 font-mono text-xs text-slate-500">{shortId(order.id)}</td>
                <td className="px-4 py-3 font-medium">{order.cliente}</td>
                <td className="px-4 py-3">{order.produto}</td>
                <td className="px-4 py-3 text-right tabular-nums">{formatCurrency(order.valor)}</td>
                <td className="px-4 py-3"><StatusBadge status={order.status} /></td>
                <td className="px-4 py-3 text-slate-600 dark:text-slate-400">{formatDateTime(order.data_criacao)}</td>
                <td className="px-4 py-3 text-right">
                  <Link
                    href={`/pedidos/${order.id}`}
                    className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm font-medium text-tmb hover:bg-tmb/10"
                  >
                    Ver <ChevronRight aria-hidden className="size-4" />
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Celular */}
      <ul className="space-y-3 md:hidden" data-testid="orders-cards">
        {orders.map((order) => (
          <li key={order.id} data-order-id={order.id}>
            <Link
              href={`/pedidos/${order.id}`}
              className={cn(
                "block rounded-xl border border-slate-200 bg-white p-4 transition-colors active:bg-slate-50 dark:border-slate-800 dark:bg-slate-900 dark:active:bg-slate-800",
                flashClass(order.id),
              )}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="truncate font-medium">{order.cliente}</p>
                  <p className="truncate text-sm text-slate-600 dark:text-slate-400">{order.produto}</p>
                </div>
                <StatusBadge status={order.status} />
              </div>
              <div className="mt-3 flex items-end justify-between text-sm">
                <span className="font-semibold tabular-nums">{formatCurrency(order.valor)}</span>
                <span className="text-xs text-slate-500">{formatDateTime(order.data_criacao)}</span>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}
