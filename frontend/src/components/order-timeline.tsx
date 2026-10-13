import { formatDateTime, formatDuration } from "@/lib/format";
import type { StatusHistoryEntry } from "@/lib/types";
import { StatusBadge } from "./status-badge";

/** Linha do tempo com o histórico de status (registrado no servidor a cada transição). */
export function OrderTimeline({ history }: { history: StatusHistoryEntry[] }) {
  if (history.length === 0) {
    return <p className="text-sm text-slate-500">Sem histórico registrado.</p>;
  }

  return (
    <ol className="relative space-y-6 border-l border-slate-200 pl-6 dark:border-slate-700" data-testid="order-timeline">
      {history.map((entry, index) => {
        const previous = history[index - 1];
        return (
          <li key={`${entry.status_novo}-${entry.alterado_em}`} className="relative">
            <span aria-hidden className="absolute -left-[1.78rem] top-1 size-3 rounded-full border-2 border-white bg-slate-400 dark:border-slate-900" />
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              {/* Historico = passado: so a etapa mais recente e a atual pode animar. */}
              <StatusBadge status={entry.status_novo} animated={index === history.length - 1 && entry.status_novo !== "Finalizado"} />
              <time className="text-sm text-slate-600 dark:text-slate-400" dateTime={entry.alterado_em}>
                {formatDateTime(entry.alterado_em)}
              </time>
              {previous && (
                <span className="text-xs text-slate-500">+{formatDuration(previous.alterado_em, entry.alterado_em)}</span>
              )}
            </div>
            <p className="mt-1 text-xs text-slate-500">
              {entry.status_anterior ? `Mudou de ${entry.status_anterior} para ${entry.status_novo}` : "Pedido criado"}
            </p>
          </li>
        );
      })}
    </ol>
  );
}
