import { CheckCircle2, Clock, Cog, Loader2 } from "lucide-react";
import { cn } from "@/lib/cn";
import type { OrderStatus } from "@/lib/types";

const STYLES: Record<OrderStatus, string> = {
  Pendente: "bg-amber-100 text-amber-800 ring-amber-600/20 dark:bg-amber-500/15 dark:text-amber-300 dark:ring-amber-400/30",
  Processando: "bg-blue-100 text-blue-800 ring-blue-600/20 dark:bg-blue-500/15 dark:text-blue-300 dark:ring-blue-400/30",
  Finalizado: "bg-emerald-100 text-emerald-800 ring-emerald-600/20 dark:bg-emerald-500/15 dark:text-emerald-300 dark:ring-emerald-400/30",
};

/** `animated=false` e usado no historico: uma etapa que ja passou nao deve parecer "em andamento". */
export function StatusIcon({ status, className, animated = true }: { status: OrderStatus; className?: string; animated?: boolean }) {
  // Em andamento: spinner. Etapa ja passada (ou ainda nao iniciada): icone estatico que nao parece "travado".
  if (status === "Processando") {
    return animated ? <Loader2 aria-hidden className={cn("animate-spin", className)} /> : <Cog aria-hidden className={className} />;
  }
  if (status === "Finalizado") return <CheckCircle2 aria-hidden className={className} />;
  return <Clock aria-hidden className={className} />;
}

export function StatusBadge({ status, className, animated = true }: { status: OrderStatus; className?: string; animated?: boolean }) {
  return (
    <span
      data-testid="status-badge"
      data-status={status}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset transition-colors duration-500",
        STYLES[status],
        className,
      )}
    >
      <StatusIcon status={status} animated={animated} className="size-3.5" />
      {status}
    </span>
  );
}
