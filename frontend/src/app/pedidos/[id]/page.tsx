import type { Metadata } from "next";
import { OrderDetailsView } from "./order-details-view";

export const metadata: Metadata = { title: "Detalhes do pedido · Gestão de Pedidos" };

export default async function OrderPage({ params }: PageProps<"/pedidos/[id]">) {
  const { id } = await params;
  return <OrderDetailsView id={id} />;
}
