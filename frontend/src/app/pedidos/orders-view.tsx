"use client";

import { ChevronLeft, ChevronRight, Inbox, RefreshCw, TriangleAlert } from "lucide-react";
import { useState } from "react";
import { CreateOrderDialog } from "@/components/create-order-dialog";
import { OrdersTable } from "@/components/orders-table";
import { useOrders } from "@/hooks/use-orders";
import { cn } from "@/lib/cn";
import type { OrderStatus } from "@/lib/types";

const PAGE_SIZE = 10;
const FILTERS: { value: OrderStatus | ""; label: string }[] = [
  { value: "", label: "Todos" },
  { value: "Pendente", label: "Pendentes" },
  { value: "Processando", label: "Processando" },
  { value: "Finalizado", label: "Finalizados" },
];

export function OrdersView() {
  const [status, setStatus] = useState<OrderStatus | "">("");
  const [page, setPage] = useState(1);
  const { data, isPending, isError, error, refetch, isFetching } = useOrders(status, page, PAGE_SIZE);

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.page_size)) : 1;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-3xl font-semibold tracking-tight"><span className="tmb-highlight">Pedidos</span></h1>
          <p className="mt-2 text-sm text-slate-600 dark:text-slate-400">
            {data ? `${data.total} ${data.total === 1 ? "pedido" : "pedidos"}` : "Carregando…"}
          </p>
        </div>
        <CreateOrderDialog />
      </div>

      <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filtrar por status">
        {FILTERS.map((f) => (
          <button
            key={f.value || "todos"}
            type="button"
            aria-pressed={status === f.value}
            onClick={() => {
              setStatus(f.value);
              setPage(1);
            }}
            className={cn(
              "rounded-full px-3.5 py-1.5 text-sm font-medium transition-colors",
              status === f.value
                ? "bg-tmb text-black"
                : "bg-white text-slate-700 ring-1 ring-slate-200 hover:bg-slate-50 dark:bg-slate-900 dark:text-slate-300 dark:ring-slate-700 dark:hover:bg-slate-800",
            )}
          >
            {f.label}
          </button>
        ))}
      </div>

      {isPending ? (
        <OrdersSkeleton />
      ) : isError ? (
        <div role="alert" className="flex flex-col items-center gap-3 rounded-xl border border-red-200 bg-red-50 p-8 text-center dark:border-red-500/30 dark:bg-red-500/10">
          <TriangleAlert aria-hidden className="size-8 text-red-500" />
          <p className="font-medium text-red-800 dark:text-red-300">Não foi possível carregar os pedidos</p>
          <p className="text-sm text-red-700 dark:text-red-400">{error instanceof Error ? error.message : "Erro desconhecido."}</p>
          <button
            type="button"
            onClick={() => refetch()}
            className="inline-flex items-center gap-2 rounded-lg bg-red-600 px-4 py-2 text-sm font-semibold text-white hover:bg-red-500"
          >
            <RefreshCw aria-hidden className={cn("size-4", isFetching && "animate-spin")} /> Tentar novamente
          </button>
        </div>
      ) : data.items.length === 0 ? (
        <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed border-slate-300 p-12 text-center dark:border-slate-700">
          <Inbox aria-hidden className="size-10 text-slate-400" />
          <p className="font-medium">Nenhum pedido {status ? `com status ${status}` : "ainda"}</p>
          <p className="text-sm text-slate-600 dark:text-slate-400">
            {status ? "Tente outro filtro." : "Clique em “Novo pedido” para criar o primeiro."}
          </p>
        </div>
      ) : (
        <>
          <OrdersTable orders={data.items} />
          {totalPages > 1 && (
            <nav aria-label="Paginação" className="flex items-center justify-between">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
                className="inline-flex items-center gap-1 rounded-lg px-3 py-2 text-sm font-medium ring-1 ring-slate-200 hover:bg-slate-50 disabled:opacity-40 dark:ring-slate-700 dark:hover:bg-slate-800"
              >
                <ChevronLeft aria-hidden className="size-4" /> Anterior
              </button>
              <span className="text-sm text-slate-600 dark:text-slate-400">
                Página {page} de {totalPages}
              </span>
              <button
                type="button"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="inline-flex items-center gap-1 rounded-lg px-3 py-2 text-sm font-medium ring-1 ring-slate-200 hover:bg-slate-50 disabled:opacity-40 dark:ring-slate-700 dark:hover:bg-slate-800"
              >
                Próxima <ChevronRight aria-hidden className="size-4" />
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}

function OrdersSkeleton() {
  return (
    <div className="space-y-3" aria-busy="true" aria-label="Carregando pedidos">
      {Array.from({ length: 6 }, (_, i) => (
        <div key={i} className="h-14 animate-pulse rounded-xl bg-slate-200/70 dark:bg-slate-800/70" />
      ))}
    </div>
  );
}
