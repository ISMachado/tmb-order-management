"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { Bot, ChevronDown, Loader2, Send, Sparkles, TriangleAlert, User } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Markdown } from "@/components/markdown";
import { api, ApiError } from "@/lib/api";
import { cn } from "@/lib/cn";
import type { ExecutedQuery } from "@/lib/types";

const EXAMPLES = [
  "Quantos pedidos temos hoje?",
  "Qual o tempo médio para aprovar os pedidos?",
  "Quantos pedidos estão pendentes?",
  "Qual o valor total de pedidos finalizados este mês?",
];

interface Message {
  id: number;
  role: "user" | "assistant" | "error";
  text: string;
  queries?: ExecutedQuery[];
}

export function AskView() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [question, setQuestion] = useState("");
  const nextId = useRef(1);
  const endRef = useRef<HTMLDivElement>(null);

  const status = useQuery({ queryKey: ["ai", "status"], queryFn: api.aiStatus, staleTime: 60_000 });

  const ask = useMutation({
    mutationFn: (q: string) => api.ask(q),
    onSuccess: (data) =>
      setMessages((m) => [...m, { id: nextId.current++, role: "assistant", text: data.resposta, queries: data.consultas_executadas }]),
    onError: (error) =>
      setMessages((m) => [
        ...m,
        {
          id: nextId.current++,
          role: "error",
          text:
            error instanceof ApiError && error.status === 429
              ? "Muitas perguntas em pouco tempo. Aguarde um minuto e tente novamente."
              : error instanceof Error
                ? error.message
                : "Erro inesperado.",
        },
      ]),
  });

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [messages, ask.isPending]);

  const disabled = status.data?.habilitado === false;

  function submit(text: string) {
    const trimmed = text.trim();
    if (!trimmed || ask.isPending || disabled) return;
    setMessages((m) => [...m, { id: nextId.current++, role: "user", text: trimmed }]);
    setQuestion("");
    ask.mutate(trimmed);
  }

  return (
    <div className="mx-auto flex max-w-3xl flex-col gap-6">
      <div>
        <h1 className="flex items-center gap-3 text-3xl font-semibold tracking-tight">
          <Sparkles aria-hidden className="size-7 shrink-0 text-tmb" />
          <span>
            Pergunte sobre os <span className="tmb-highlight">pedidos</span>
          </span>
        </h1>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-400">
          Faça perguntas em linguagem natural. A IA consulta os dados reais do banco (somente leitura) e responde.
          {status.data?.modelo && <span className="ml-1 text-xs text-slate-500">Modelo: {status.data.modelo}.</span>}
        </p>
      </div>

      {disabled && (
        <div role="status" className="flex gap-3 rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-500/40 dark:bg-amber-500/10 dark:text-amber-200">
          <TriangleAlert aria-hidden className="mt-0.5 size-5 shrink-0" />
          <p>
            O módulo de IA está desativado porque não há chave de API configurada. Defina <code className="font-mono">ANTHROPIC_API_KEY</code> no
            arquivo <code className="font-mono">.env</code> e reinicie a API. O restante do sistema funciona normalmente.
          </p>
        </div>
      )}

      <div className="flex flex-wrap gap-2" aria-label="Perguntas de exemplo">
        {EXAMPLES.map((example) => (
          <button
            key={example}
            type="button"
            disabled={disabled || ask.isPending}
            onClick={() => submit(example)}
            className="rounded-full bg-white px-3.5 py-1.5 text-sm text-slate-700 ring-1 ring-slate-200 transition hover:bg-tmb/10 hover:text-tmb disabled:cursor-not-allowed disabled:opacity-50 dark:bg-slate-900 dark:text-slate-300 dark:ring-slate-700 hover:bg-tmb/10"
          >
            {example}
          </button>
        ))}
      </div>

      <div
        className="min-h-64 space-y-4 rounded-2xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900"
        role="log"
        aria-live="polite"
        aria-label="Conversa"
      >
        {messages.length === 0 && !ask.isPending && (
          <p className="py-12 text-center text-sm text-slate-500">Escolha uma pergunta acima ou escreva a sua.</p>
        )}
        {messages.map((m) => (
          <MessageBubble key={m.id} message={m} />
        ))}
        {ask.isPending && (
          <div className="flex items-center gap-2 text-sm text-slate-500" data-testid="ai-loading">
            <Loader2 aria-hidden className="size-4 animate-spin" /> Consultando os pedidos…
          </div>
        )}
        <div ref={endRef} />
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          submit(question);
        }}
        className="flex gap-2"
      >
        <label htmlFor="pergunta" className="sr-only">Sua pergunta</label>
        <input
          id="pergunta"
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          maxLength={500}
          disabled={disabled}
          placeholder="Ex.: Qual cliente tem o maior valor em pedidos?"
          className="flex-1 rounded-lg border border-slate-300 bg-white px-4 py-2.5 text-sm shadow-sm outline-none transition focus:border-tmb focus:ring-2 focus:ring-tmb/30 disabled:opacity-50 dark:border-slate-700 dark:bg-slate-900"
        />
        <button
          type="submit"
          disabled={disabled || ask.isPending || !question.trim()}
          className="inline-flex items-center gap-2 rounded-lg bg-tmb px-4 py-2.5 text-sm font-semibold text-black transition hover:bg-tmb-hover disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Send aria-hidden className="size-4" /> Perguntar
        </button>
      </form>
    </div>
  );
}

function MessageBubble({ message }: { message: Message }) {
  const isUser = message.role === "user";
  const isError = message.role === "error";

  return (
    <div className={cn("flex gap-3", isUser && "flex-row-reverse")}>
      <span
        aria-hidden
        className={cn(
          "flex size-8 shrink-0 items-center justify-center rounded-full text-white",
          isUser ? "bg-slate-500" : isError ? "bg-red-500" : "bg-tmb text-black",
        )}
      >
        {isUser ? <User className="size-4" /> : isError ? <TriangleAlert className="size-4" /> : <Bot className="size-4" />}
      </span>
      <div className={cn("max-w-[85%] space-y-2", isUser && "text-right")}>
        <div
          className={cn(
            "inline-block max-w-full rounded-2xl px-4 py-2.5 text-left text-sm leading-relaxed",
            isUser && "whitespace-pre-wrap bg-tmb text-black",
            !isUser && !isError && "bg-slate-100 dark:bg-slate-800",
            isError && "whitespace-pre-wrap bg-red-50 text-red-800 dark:bg-red-500/10 dark:text-red-300",
          )}
        >
          {/* Resposta da IA vem em Markdown; pergunta do usuario e erros sao texto puro. */}
          {isUser || isError ? message.text : <Markdown>{message.text}</Markdown>}
        </div>
        {message.queries && message.queries.length > 0 && <QueriesDetails queries={message.queries} />}
      </div>
    </div>
  );
}

/** Transparência: mostra quais consultas foram executadas para chegar na resposta. */
function QueriesDetails({ queries }: { queries: ExecutedQuery[] }) {
  return (
    <details className="group text-left text-xs text-slate-600 dark:text-slate-400">
      <summary className="inline-flex cursor-pointer list-none items-center gap-1 font-medium hover:text-tmb">
        <ChevronDown aria-hidden className="size-3.5 transition group-open:rotate-180" />
        {queries.length} {queries.length === 1 ? "consulta executada" : "consultas executadas"}
      </summary>
      <ul className="mt-2 space-y-2">
        {queries.map((q, i) => (
          <li key={i} className="rounded-lg bg-slate-50 p-3 dark:bg-slate-800/60">
            <p className="font-mono font-semibold">{q.ferramenta}</p>
            <p className="mt-1 break-all font-mono text-slate-500">{q.parametros}</p>
            {q.sql && <pre className="mt-2 overflow-x-auto whitespace-pre-wrap font-mono text-slate-500">{q.sql}</pre>}
          </li>
        ))}
      </ul>
    </details>
  );
}
