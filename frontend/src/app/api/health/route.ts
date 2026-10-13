// Usado pelo healthcheck do Docker Compose.
export function GET() {
  return Response.json({ status: "Healthy" });
}
