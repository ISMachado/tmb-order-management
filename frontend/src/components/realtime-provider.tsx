"use client";

import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { toast } from "sonner";
import { API_URL } from "@/lib/api";
import { applyStatusEvent, findCachedOrder, queryKeys } from "@/lib/cache";
import { shortId } from "@/lib/format";
import { isOrderStatus } from "@/lib/status";
import type { OrderStatusChangedEvent } from "@/lib/types";

export type ConnectionStatus = "connecting" | "connected" | "reconnecting" | "offline";

interface RealtimeContextValue {
  connection: ConnectionStatus;
  /** Ids de pedidos que acabaram de mudar de status (usado para destacar a linha por alguns segundos). */
  flashing: ReadonlySet<string>;
}

export const RealtimeContext = createContext<RealtimeContextValue>({
  connection: "connecting",
  flashing: new Set(),
});

const FLASH_DURATION_MS = 2500;
const RETRY_DELAY_MS = 10_000;

/**
 * Mantém UMA conexão SignalR para o app inteiro. O SignalR negocia sozinho o melhor transporte
 * (WebSocket -> Server-Sent Events -> Long Polling). Se a conexão cair de vez, os hooks de dados
 * passam a fazer polling (ver useRefetchInterval) até a conexão voltar.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [connection, setConnection] = useState<ConnectionStatus>("connecting");
  const [flashing, setFlashing] = useState<ReadonlySet<string>>(new Set());
  const invalidateTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const flash = useCallback((orderId: string) => {
    setFlashing((prev) => new Set(prev).add(orderId));
    setTimeout(() => {
      setFlashing((prev) => {
        const next = new Set(prev);
        next.delete(orderId);
        return next;
      });
    }, FLASH_DURATION_MS);
  }, []);

  useEffect(() => {
    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;

    const hub: HubConnection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/orders`)
      .withAutomaticReconnect([0, 2000, 5000, 10_000, 20_000])
      .configureLogging(LogLevel.Warning)
      .build();

    // Reconcilia com o servidor (fonte da verdade) logo depois de um burst de eventos.
    const scheduleResync = () => {
      if (invalidateTimer.current) clearTimeout(invalidateTimer.current);
      invalidateTimer.current = setTimeout(() => queryClient.invalidateQueries({ queryKey: queryKeys.all }), 600);
    };

    hub.on("OrderStatusChanged", (payload: OrderStatusChangedEvent) => {
      if (!payload || !isOrderStatus(payload.status_novo)) return;

      const cached = findCachedOrder(queryClient, payload.order_id);
      const isNew = applyStatusEvent(queryClient, payload);
      if (isNew) {
        flash(payload.order_id);
        const label = cached ? `${cached.cliente} · ${cached.produto}` : `Pedido ${shortId(payload.order_id)}`;
        if (payload.status_novo === "Finalizado") {
          toast.success("Pedido finalizado", { description: label });
        } else if (payload.status_novo === "Processando") {
          toast.info("Pedido em processamento", { description: label });
        }
      }
      scheduleResync();
    });

    hub.onreconnecting(() => !disposed && setConnection("reconnecting"));
    hub.onreconnected(() => {
      if (disposed) return;
      setConnection("connected");
      // Eventos podem ter sido perdidos enquanto estava offline: recarrega do servidor.
      queryClient.invalidateQueries({ queryKey: queryKeys.all });
    });
    hub.onclose(() => {
      if (disposed) return;
      setConnection("offline");
      retryTimer = setTimeout(start, RETRY_DELAY_MS);
    });

    async function start() {
      if (disposed || hub.state !== HubConnectionState.Disconnected) return;
      try {
        await hub.start();
        if (disposed) return;
        setConnection("connected");
        queryClient.invalidateQueries({ queryKey: queryKeys.all });
      } catch {
        if (disposed) return;
        setConnection("offline");
        retryTimer = setTimeout(start, RETRY_DELAY_MS);
      }
    }

    void start();

    return () => {
      disposed = true;
      if (retryTimer) clearTimeout(retryTimer);
      if (invalidateTimer.current) clearTimeout(invalidateTimer.current);
      void hub.stop();
    };
  }, [queryClient, flash]);

  const value = useMemo(() => ({ connection, flashing }), [connection, flashing]);
  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime(): RealtimeContextValue {
  return useContext(RealtimeContext);
}

/**
 * Intervalo de polling dos dados: lento (rede de segurança) com tempo real ativo,
 * rápido (3s) quando a conexão em tempo real não está disponível.
 */
export function useRefetchInterval(): number {
  const { connection } = useRealtime();
  return connection === "connected" ? 30_000 : 3_000;
}
