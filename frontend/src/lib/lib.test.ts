import { describe, expect, it } from "vitest";
import { formatCurrency, formatDuration, parseMoneyInput, shortId } from "./format";
import { createOrderSchema } from "./schemas";
import { advanceStatus, isOrderStatus, STATUS_RANK } from "./status";

describe("parseMoneyInput", () => {
  it.each([
    ["1299,90", 1299.9],
    ["1.299,90", 1299.9],
    ["1299.90", 1299.9],
    ["1,299.90", 1299.9],
    ["R$ 10,50", 10.5],
    ["10", 10],
    ["0,5", 0.5],
    ["1.000", 1000],
    ["2.500", 2500],
    ["1.299.900", 1299900],
    ["10.5", 10.5],
  ])("interpreta %s como %d", (input, expected) => {
    expect(parseMoneyInput(input)).toBe(expected);
  });

  it.each(["", "abc", "R$"])("rejeita %j", (input) => {
    expect(parseMoneyInput(input)).toBeNaN();
  });
});

describe("format", () => {
  it("formata moeda em reais", () => {
    expect(formatCurrency(1299.9).replace(/\s/g, " ")).toBe("R$ 1.299,90");
  });

  it("abrevia ids", () => {
    expect(shortId("01a12147-bc07-7fd3-b1cb-32272237a043")).toBe("01a1…a043");
    expect(shortId("abc")).toBe("abc");
  });

  it("formata duração", () => {
    expect(formatDuration("2026-10-09T12:00:00Z", "2026-10-09T12:00:00.440Z")).toBe("440 ms");
    expect(formatDuration("2026-10-09T12:00:00Z", "2026-10-09T12:00:05Z")).toBe("5,0 s");
    expect(formatDuration("2026-10-09T12:00:00Z", "2026-10-09T12:01:30Z")).toBe("1 min 30 s");
  });
});

describe("status", () => {
  it("nunca regride o status por causa de eventos atrasados", () => {
    expect(advanceStatus("Finalizado", "Processando")).toBe("Finalizado");
    expect(advanceStatus("Processando", "Pendente")).toBe("Processando");
    expect(advanceStatus("Pendente", "Processando")).toBe("Processando");
    expect(advanceStatus("Processando", "Finalizado")).toBe("Finalizado");
  });

  it("valida status conhecidos", () => {
    expect(isOrderStatus("Pendente")).toBe(true);
    expect(isOrderStatus("Cancelado")).toBe(false);
    expect(STATUS_RANK.Pendente).toBeLessThan(STATUS_RANK.Processando);
    expect(STATUS_RANK.Processando).toBeLessThan(STATUS_RANK.Finalizado);
  });
});

describe("createOrderSchema", () => {
  const valid = { cliente: "Ana", produto: "Notebook", valor: "1.299,90" };

  it("aceita dados válidos e converte o valor para número", () => {
    const result = createOrderSchema.safeParse(valid);
    expect(result.success).toBe(true);
    expect(result.data).toEqual({ cliente: "Ana", produto: "Notebook", valor: 1299.9 });
  });

  it("remove espaços das pontas", () => {
    expect(createOrderSchema.parse({ ...valid, cliente: "  Ana  " }).cliente).toBe("Ana");
  });

  it.each([
    [{ ...valid, cliente: "" }, "cliente"],
    [{ ...valid, produto: "   " }, "produto"],
    [{ ...valid, valor: "" }, "valor"],
    [{ ...valid, valor: "abc" }, "valor"],
    [{ ...valid, valor: "0" }, "valor"],
    [{ ...valid, valor: "-5" }, "valor"],
    [{ ...valid, valor: "10,999" }, "valor"],
    [{ ...valid, cliente: "x".repeat(151) }, "cliente"],
  ])("rejeita %j", (input, field) => {
    const result = createOrderSchema.safeParse(input);
    expect(result.success).toBe(false);
    expect(result.error?.issues.some((i) => i.path[0] === field)).toBe(true);
  });
});
