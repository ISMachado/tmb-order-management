"use client";

import { ArrowLeft, SearchX, TriangleAlert } from "lucide-react";
import Link from "next/link";
import { OrderStepper } from "@/components/order-stepper";
import { OrderTimeline } from "@/components/order-timeline";
import { useRealtime } from "@/components/realtime-provider";
import { StatusBadge } from "@/components/status-badge";
import { useOrder } from "@/hooks/use-orders";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import { formatCurrency, formatDateTime } from "@/lib/format";

export function OrderDetailsView({ id }: { id: string }) {
  const { data: order, isPending, isError, error } = useOrder(id);
  const { flashing } = useRealtime();

  const back = (
    <Link href="/pedidos" className="inline-flex items-center gap-2 text-sm font-medium text-tmb hover:underline">
      <ArrowLeft aria-hidden className="size-4" /> Voltar para pedidos
    </Link>
  );

  if (isPending) {
    return (
      <div className="space-y-6" aria-busy="true">
        {back}
        <div className="h-40 animate-pulse rounded-2xl bg-slate-200/70 dark:bg-slate-800/70" />
        <div className="h-32 animate-pulse rounded-2xl bg-slate-200/70 dark:bg-slate-800/70" />
      </div>
    );
  }

  if (isError) {
    const notFound = error instanceof ApiError && error.status === 404;
    const Icon = notFound ? SearchX : TriangleAlert;
    return (
      <div className="space-y-6">
        {back}
        <div role="alert" className="flex flex-col items-center gap-3 rounded-xl border border-slate-200 p-10 text-center dark:border-slate-800">
          <Icon aria-hidden className="size-10 text-slate-400" />
          <p className="font-medium">{notFound ? "Pedido não encontrado" : "Não foi possível carregar o pedido"}</p>
          <p className="text-sm text-slate-600 dark:text-slate-400">{error instanceof Error ? error.message : "Erro desconhecido."}</p>
        </div>
      </div>
    );
  }

  const details: [string, string][] = [
    ["Cliente", order.cliente],
    ["Produto", order.produto],
    ["Valor", formatCurrency(order.valor)],
    ["Criado em", formatDateTime(order.data_criacao)],
    ["Processamento iniciado", formatDateTime(order.processamento_iniciado_em)],
    ["Finalizado em", formatDateTime(order.finalizado_em)],
  ];

  return (
    <div className="space-y-8">
      {back}

      <header className={cn("flex flex-wrap items-start justify-between gap-4 rounded-2xl p-1", flashing.has(order.id) && "animate-row-flash")}>
        <div>
          <h1 className="text-3xl font-semibold tracking-tight"><span className="tmb-highlight">Pedido</span></h1>
          <p className="mt-1 break-all font-mono text-xs text-slate-500">{order.id}</p>
        </div>
        <StatusBadge status={order.status} className="px-3.5 py-1.5 text-sm" />
      </header>

      <section aria-labelledby="progresso">
        <h2 id="progresso" className="mb-3 text-sm font-semibold uppercase tracking-wide text-slate-500">Progresso</h2>
        <OrderStepper status={order.status} />
      </section>

      <div className="grid gap-8 lg:grid-cols-2">
        <section aria-labelledby="dados" className="rounded-2xl border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-900">
          <h2 id="dados" className="mb-4 text-sm font-semibold uppercase tracking-wide text-slate-500">Dados do pedido</h2>
          <dl className="grid gap-4 sm:grid-cols-2">
            {details.map(([label, value]) => (
              <div key={label}>
                <dt className="text-xs text-slate-500">{label}</dt>
                <dd className="mt-0.5 font-medium">{value}</dd>
              </div>
            ))}
          </dl>
        </section>

        <section aria-labelledby="historico" className="rounded-2xl border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-900">
          <h2 id="historico" className="mb-4 text-sm font-semibold uppercase tracking-wide text-slate-500">Histórico de status</h2>
          <OrderTimeline history={order.historico} />
        </section>
      </div>
    </div>
  );
}
