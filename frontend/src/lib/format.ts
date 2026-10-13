const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

const dateTime = new Intl.DateTimeFormat("pt-BR", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
});

export function formatCurrency(value: number): string {
  return currency.format(value);
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? "—" : dateTime.format(date);
}

/** Id curto para exibição (os primeiros e últimos caracteres do GUID). */
export function shortId(id: string): string {
  return id.length > 8 ? `${id.slice(0, 4)}…${id.slice(-4)}` : id;
}

/**
 * Converte o texto digitado (aceita "1.234,56", "1234,56", "1234.56" e "1.000") em número.
 * Retorna NaN quando não é um valor válido.
 */
export function parseMoneyInput(text: string): number {
  const cleaned = text.replace(/[^\d.,-]/g, "").trim();
  if (!cleaned) return NaN;

  const lastComma = cleaned.lastIndexOf(",");
  const lastDot = cleaned.lastIndexOf(".");
  let normalized: string;

  if (lastComma > -1 && lastDot > -1) {
    // O separador que aparece por último é o decimal.
    normalized =
      lastComma > lastDot
        ? cleaned.replace(/\./g, "").replace(",", ".")
        : cleaned.replace(/,/g, "");
  } else if (lastComma > -1) {
    normalized = cleaned.replace(",", ".");
  } else if (/^\d{1,3}(\.\d{3})+$/.test(cleaned)) {
    // Em pt-BR "1.000" e "2.500" sao mil e dois mil e quinhentos (ponto = milhar), nao 1,00 e 2,50.
    normalized = cleaned.replace(/\./g, "");
  } else {
    normalized = cleaned;
  }

  return Number(normalized);
}

/** Duração entre duas datas ISO em texto curto ("5,2 s"). */
export function formatDuration(fromIso: string, toIso: string): string {
  const ms = new Date(toIso).getTime() - new Date(fromIso).getTime();
  if (!Number.isFinite(ms) || ms < 0) return "—";
  if (ms < 1000) return `${ms} ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(1).replace(".", ",")} s`;
  return `${Math.floor(seconds / 60)} min ${Math.round(seconds % 60)} s`;
}
