import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";

/**
 * Renderiza a resposta da IA (Markdown: negrito, listas, tabelas). O react-markdown NAO interpreta HTML bruto,
 * entao um texto malicioso vindo dos dados (ex.: nome de cliente com <script>) nunca vira HTML.
 */
const components: Components = {
  p: ({ children }) => <p className="[&:not(:first-child)]:mt-3">{children}</p>,
  strong: ({ children }) => <strong className="font-semibold text-white">{children}</strong>,
  ul: ({ children }) => <ul className="mt-3 list-disc space-y-1 pl-5">{children}</ul>,
  ol: ({ children }) => <ol className="mt-3 list-decimal space-y-1 pl-5">{children}</ol>,
  code: ({ children }) => <code className="rounded bg-black/40 px-1 py-0.5 font-mono text-xs">{children}</code>,
  a: ({ children }) => <span className="text-tmb">{children}</span>, // sem links clicaveis vindos da IA
  // Imagens markdown fariam o navegador requisitar uma URL arbitraria (exfiltracao via prompt injection).
  img: () => null,
  table: ({ children }) => (
    <div className="mt-3 overflow-x-auto rounded-lg border border-white/10">
      <table className="w-full border-collapse text-left text-xs sm:text-sm">{children}</table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-white/5 text-xs uppercase tracking-wide text-slate-400">{children}</thead>,
  th: ({ children }) => <th className="px-3 py-2 font-medium">{children}</th>,
  td: ({ children }) => <td className="border-t border-white/10 px-3 py-2">{children}</td>,
};

export function Markdown({ children }: { children: string }) {
  return (
    <ReactMarkdown remarkPlugins={[remarkGfm]} components={components} skipHtml>
      {children}
    </ReactMarkdown>
  );
}
