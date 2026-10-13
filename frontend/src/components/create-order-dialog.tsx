"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Plus, X } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { useCreateOrder } from "@/hooks/use-orders";
import { ApiError } from "@/lib/api";
import { formatCurrency } from "@/lib/format";
import { createOrderSchema, type CreateOrderFormInput, type CreateOrderFormOutput } from "@/lib/schemas";

const inputClass =
  "w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm shadow-sm outline-none transition focus:border-tmb focus:ring-2 focus:ring-tmb/30 aria-[invalid=true]:border-red-500 dark:border-slate-700 dark:bg-slate-900";

export function CreateOrderDialog() {
  const [open, setOpen] = useState(false);
  const router = useRouter();
  const createOrder = useCreateOrder();

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<CreateOrderFormInput, unknown, CreateOrderFormOutput>({
    resolver: zodResolver(createOrderSchema),
    defaultValues: { cliente: "", produto: "", valor: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      const order = await createOrder.mutateAsync(values);
      toast.success("Pedido criado", {
        description: `${order.cliente} · ${formatCurrency(order.valor)}`,
        action: { label: "Ver detalhes", onClick: () => router.push(`/pedidos/${order.id}`) },
      });
      reset();
      setOpen(false);
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        // Erros de validação do servidor aparecem junto ao campo correspondente.
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field === "cliente" || field === "produto" || field === "valor") {
            setError(field, { message: messages[0] });
          }
        }
        return;
      }
      toast.error("Não foi possível criar o pedido", {
        description: error instanceof Error ? error.message : "Tente novamente.",
      });
    }
  });

  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger asChild>
        <button
          type="button"
          className="inline-flex items-center gap-2 rounded-lg bg-tmb px-4 py-2 text-sm font-semibold text-black shadow-sm transition hover:bg-tmb-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-tmb"
        >
          <Plus aria-hidden className="size-4" /> Novo pedido
        </button>
      </Dialog.Trigger>

      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-40 bg-slate-950/50 backdrop-blur-sm" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-50 w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl bg-white p-6 shadow-xl dark:bg-slate-900">
          <div className="flex items-start justify-between">
            <div>
              <Dialog.Title className="text-lg font-semibold">Novo pedido</Dialog.Title>
              <Dialog.Description className="mt-1 text-sm text-slate-600 dark:text-slate-400">
                O pedido é criado como Pendente e processado automaticamente.
              </Dialog.Description>
            </div>
            <Dialog.Close className="rounded-md p-1 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" aria-label="Fechar">
              <X aria-hidden className="size-5" />
            </Dialog.Close>
          </div>

          <form onSubmit={onSubmit} noValidate className="mt-5 space-y-4">
            <Field htmlFor="cliente" label="Cliente" error={errors.cliente?.message}>
              <input {...register("cliente")} id="cliente" autoComplete="off" placeholder="Nome do cliente" aria-invalid={!!errors.cliente} className={inputClass} />
            </Field>
            <Field htmlFor="produto" label="Produto" error={errors.produto?.message}>
              <input {...register("produto")} id="produto" autoComplete="off" placeholder="Produto" aria-invalid={!!errors.produto} className={inputClass} />
            </Field>
            <Field htmlFor="valor" label="Valor (R$)" error={errors.valor?.message}>
              <input {...register("valor")} id="valor" inputMode="decimal" autoComplete="off" placeholder="1.299,90" aria-invalid={!!errors.valor} className={inputClass} />
            </Field>

            <div className="flex justify-end gap-3 pt-2">
              <Dialog.Close className="rounded-lg px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800">
                Cancelar
              </Dialog.Close>
              <button
                type="submit"
                disabled={createOrder.isPending}
                className="inline-flex items-center gap-2 rounded-lg bg-tmb px-4 py-2 text-sm font-semibold text-black transition hover:bg-tmb-hover disabled:cursor-not-allowed disabled:opacity-60"
              >
                {createOrder.isPending && <Loader2 aria-hidden className="size-4 animate-spin" />}
                {createOrder.isPending ? "Criando…" : "Criar pedido"}
              </button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function Field({ htmlFor, label, error, children }: { htmlFor: string; label: string; error?: string; children: React.ReactNode }) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1 block text-sm font-medium">{label}</label>
      {children}
      {error && (
        <p role="alert" className="mt-1 text-xs text-red-600 dark:text-red-400">
          {error}
        </p>
      )}
    </div>
  );
}
