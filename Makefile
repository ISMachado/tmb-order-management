.DEFAULT_GOAL := help
.PHONY: help env up down logs ps test test-backend test-frontend reset

help: ## Mostra os comandos disponiveis
	@grep -E '^[a-z-]+:.*##' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*## "}; {printf "  make %-15s %s\n", $$1, $$2}'

env: ## Cria o arquivo .env a partir do modelo (se ainda nao existir)
	@test -f .env || (cp .env.example .env && echo ".env criado. Abra-o e, se quiser usar a IA, preencha ANTHROPIC_API_KEY.")

up: env ## Sobe TODO o ambiente (API, worker, frontend, banco, pgAdmin, RabbitMQ, Jaeger)
	docker compose up -d --build --wait
	@echo ""
	@echo "Pronto! Veja as URLs de cada servico na tabela do README (portas configuraveis no .env)."

down: ## Desliga o ambiente (mantem os dados)
	docker compose down

logs: ## Acompanha os logs de todos os servicos
	docker compose logs -f --tail=100

ps: ## Mostra o estado e a saude dos servicos
	docker compose ps

test: test-backend test-frontend ## Roda TODOS os testes (o Docker precisa estar aberto)

test-backend: ## Testes do backend: unitarios, integracao (containers reais) e golden
	cd backend && dotnet test Orders.slnx

test-frontend: ## Testes do frontend (Vitest) + lint + tipos
	cd frontend && npm ci --silent && npm run lint && npm run typecheck && npm test

reset: ## Desliga tudo e APAGA os dados (volumes)
	docker compose down -v
