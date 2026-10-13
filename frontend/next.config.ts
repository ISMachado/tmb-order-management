import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Gera um servidor autocontido (.next/standalone) para uma imagem Docker enxuta.
  output: "standalone",
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
