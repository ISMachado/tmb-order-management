"use client";

import { MessageSquareText, Package } from "lucide-react";
import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/cn";
import { RealtimeIndicator } from "./realtime-indicator";

const links = [
  { href: "/pedidos", label: "Pedidos", icon: Package },
  { href: "/perguntas", label: "Pergunte à IA", icon: MessageSquareText },
];

export function AppHeader() {
  const pathname = usePathname();

  return (
    <header className="sticky top-0 z-30 border-b border-white/10 bg-black/90 backdrop-blur">
      <div className="mx-auto flex h-[72px] max-w-6xl items-center justify-between gap-4 px-4">
        <div className="flex items-center gap-4 sm:gap-6">
          <Link href="/pedidos" className="flex items-center gap-4" aria-label="TMB — Gestão de Pedidos">
            {/* Logo oficial da TMB (versao branca, para fundo escuro), de tmb.com.br. */}
            <Image src="/brand/tmb-logo.webp" alt="TMB" width={105} height={48} priority className="h-9 w-auto" />
            <span aria-hidden className="hidden h-6 w-px bg-white/20 sm:block" />
            <span className="font-brand hidden text-sm font-semibold tracking-tight sm:block">Gestão de Pedidos</span>
          </Link>
          <nav className="flex items-center gap-1" aria-label="Principal">
            {links.map(({ href, label, icon: Icon }) => {
              const active = pathname.startsWith(href);
              return (
                <Link
                  key={href}
                  href={href}
                  aria-current={active ? "page" : undefined}
                  className={cn(
                    "inline-flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                    active ? "bg-tmb/15 text-tmb" : "text-slate-600 hover:bg-white/10 hover:text-white",
                  )}
                >
                  <Icon aria-hidden className="size-4" />
                  <span className="hidden sm:inline">{label}</span>
                </Link>
              );
            })}
          </nav>
        </div>
        <RealtimeIndicator />
      </div>
    </header>
  );
}
