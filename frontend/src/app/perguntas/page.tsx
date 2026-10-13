import type { Metadata } from "next";
import { AskView } from "./ask-view";

export const metadata: Metadata = { title: "Pergunte sobre os pedidos · Gestão de Pedidos" };

export default function AskPage() {
  return <AskView />;
}
