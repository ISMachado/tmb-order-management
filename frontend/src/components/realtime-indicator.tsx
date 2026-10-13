"use client";

import { cn } from "@/lib/cn";
import { useRealtime, type ConnectionStatus } from "./realtime-provider";

const CONFIG: Record<ConnectionStatus, { label: string; hint: string; dot: string; text: string }> = {
  connected: {
    label: "Tempo real",
    hint: "Conectado: o status dos pedidos atualiza instantaneamente.",
    dot: "bg-emerald-500",
    text: "text-emerald-700 dark:text-emerald-300",
  },
  connecting: {
    label: "Conectando…",
    hint: "Estabelecendo a conexão em tempo real.",
    dot: "bg-slate-400 animate-pulse",
    text: "text-slate-600 dark:text-slate-300",
  },
  reconnecting: {
    label: "Reconectando…",
    hint: "Conexão perdida. Atualizando a cada 3 segundos enquanto reconecta.",
    dot: "bg-amber-500 animate-pulse",
    text: "text-amber-700 dark:text-amber-300",
  },
  offline: {
    label: "Atualização periódica",
    hint: "Tempo real indisponível. Os dados são atualizados a cada 3 segundos.",
    dot: "bg-amber-500",
    text: "text-amber-700 dark:text-amber-300",
  },
};

export function RealtimeIndicator() {
  const { connection } = useRealtime();
  const c = CONFIG[connection];
  return (
    <span
      role="status"
      title={c.hint}
      data-testid="realtime-indicator"
      data-connection={connection}
      className={cn("inline-flex items-center gap-2 text-xs font-medium", c.text)}
    >
      <span aria-hidden className={cn("size-2 rounded-full", c.dot)} />
      {c.label}
    </span>
  );
}
