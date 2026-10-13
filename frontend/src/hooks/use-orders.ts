"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef } from "react";
import { useRefetchInterval } from "@/components/realtime-provider";
import { api, ApiError } from "@/lib/api";
import { queryKeys } from "@/lib/cache";
import type { CreateOrderInput, OrderStatus } from "@/lib/types";

export function useOrders(status: OrderStatus | "", page: number, pageSize: number) {
  const refetchInterval = useRefetchInterval();
  return useQuery({
    queryKey: queryKeys.list(status, page, pageSize),
    queryFn: () => api.listOrders({ status, page, pageSize }),
    placeholderData: keepPreviousData,
    refetchInterval,
  });
}

export function useOrder(id: string) {
  const refetchInterval = useRefetchInterval();
  return useQuery({
    queryKey: queryKeys.detail(id),
    queryFn: () => api.getOrder(id),
    refetchInterval,
    // 404 não adianta tentar de novo.
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 2,
  });
}

/**
 * Cria um pedido. A Idempotency-Key é gerada uma vez por "tentativa de envio" e só é renovada após sucesso:
 * se o usuário clicar duas vezes ou o envio for repetido por falha de rede, o servidor não cria pedido duplicado.
 */
export function useCreateOrder() {
  const queryClient = useQueryClient();
  const keyRef = useRef<string>(crypto.randomUUID());

  return useMutation({
    mutationFn: (input: CreateOrderInput) => api.createOrder(input, keyRef.current),
    onSuccess: () => {
      keyRef.current = crypto.randomUUID();
      return queryClient.invalidateQueries({ queryKey: queryKeys.lists() });
    },
    onError: (error) => {
      // Falha de rede (status 0): o servidor pode ter criado o pedido, então mantemos a chave para o reenvio.
      // Qualquer outro erro (ex.: validação) descarta a chave, pois o usuário vai corrigir os dados.
      if (!(error instanceof ApiError && error.status === 0)) {
        keyRef.current = crypto.randomUUID();
      }
    },
  });
}
