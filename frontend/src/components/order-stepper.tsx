import { Check } from "lucide-react";
import { cn } from "@/lib/cn";
import { STATUS_DESCRIPTION, STATUS_FLOW, STATUS_RANK } from "@/lib/status";
import type { OrderStatus } from "@/lib/types";
import { StatusIcon } from "./status-badge";

/** Etapas Pendente -> Processando -> Finalizado, destacando a etapa atual. */
export function OrderStepper({ status }: { status: OrderStatus }) {
  const currentRank = STATUS_RANK[status];

  return (
    <ol className="grid gap-4 sm:grid-cols-3" aria-label="Progresso do pedido">
      {STATUS_FLOW.map((step) => {
        const rank = STATUS_RANK[step];
        const done = rank < currentRank || (rank === currentRank && step === "Finalizado");
        const current = rank === currentRank && !done;

        return (
          <li
            key={step}
            aria-current={rank === currentRank ? "step" : undefined}
            data-step={step}
            data-state={done ? "done" : current ? "current" : "todo"}
            className={cn(
              "relative flex items-start gap-3 rounded-xl border p-4 transition-all duration-500",
              done && "border-emerald-300 bg-emerald-50 dark:border-emerald-500/40 dark:bg-emerald-500/10",
              current && "border-blue-300 bg-blue-50 shadow-sm dark:border-blue-500/40 dark:bg-blue-500/10",
              !done && !current && "border-slate-200 bg-slate-50 opacity-60 dark:border-slate-700 dark:bg-slate-800/40",
            )}
          >
            <span
              className={cn(
                "flex size-8 shrink-0 items-center justify-center rounded-full text-white",
                done && "bg-emerald-500",
                current && "bg-blue-500",
                !done && !current && "bg-slate-400",
              )}
            >
              {done ? <Check aria-hidden className="size-4" /> : <StatusIcon status={step} animated={current} className="size-4" />}
            </span>
            <div>
              <p className="text-sm font-semibold">{step}</p>
              <p className="mt-0.5 text-xs text-slate-600 dark:text-slate-400">{STATUS_DESCRIPTION[step]}</p>
            </div>
          </li>
        );
      })}
    </ol>
  );
}
