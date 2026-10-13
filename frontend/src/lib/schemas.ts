import { z } from "zod";
import { parseMoneyInput } from "./format";

/** Espelha as regras do backend (FluentValidation): textos até 150 caracteres e valor > 0 com 2 casas. */
export const createOrderSchema = z.object({
  cliente: z
    .string()
    .trim()
    .min(1, "Informe o cliente.")
    .max(150, "O cliente deve ter no máximo 150 caracteres."),
  produto: z
    .string()
    .trim()
    .min(1, "Informe o produto.")
    .max(150, "O produto deve ter no máximo 150 caracteres."),
  valor: z
    .string()
    .trim()
    .min(1, "Informe o valor.")
    .transform((text, ctx) => {
      const value = parseMoneyInput(text);
      if (!Number.isFinite(value)) {
        ctx.addIssue({ code: "custom", message: "Digite um valor válido, por exemplo 1.299,90." });
        return z.NEVER;
      }
      if (value <= 0) {
        ctx.addIssue({ code: "custom", message: "O valor deve ser maior que zero." });
        return z.NEVER;
      }
      if (Math.round(value * 100) / 100 !== value) {
        ctx.addIssue({ code: "custom", message: "Use no máximo 2 casas decimais." });
        return z.NEVER;
      }
      return value;
    }),
});

export type CreateOrderFormInput = z.input<typeof createOrderSchema>;
export type CreateOrderFormOutput = z.output<typeof createOrderSchema>;
