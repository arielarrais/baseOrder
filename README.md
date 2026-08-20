# baseOrder

Sistema de envio de ordens financeiras via protocolo FIX 4.4, com arquitetura Clean Architecture/DDD e comunicação event-driven assíncrona via RabbitMQ entre OrderGenerator (web) e OrderAccumulator (worker).

> This is a challenge by [Coodesh](https://coodesh.com/)

## Stack

- **.NET 8.0** / C# 12
- **ASP.NET Core Razor Pages** (OrderGenerator.Web)
- **.NET Worker Service** (OrderAccumulator.Worker)
- **FIX 4.4** via QuickFIXn 1.14.0
- **RabbitMQ** — comunicação event-driven assíncrona entre serviços
- **Polly 8** (retry com backoff exponencial)
- **Serilog** (structured logging, Console + File)
- **xUnit** (testes unitários)

## Arquitetura

```
src/
├── Shared/
│   ├── Shared.Domain/
│   │   ├── ValueObjects/              # Money
│   │   └── Events/                    # OrderCreatedEvent, OrderProcessedEvent
│   └── Shared.Infrastructure/
│       ├── Fix/                       # IFixClient + QuickFIXn
│       └── Messaging/                 # IEventBroker, RabbitMQEventBroker
├── OrderGenerator/
│   ├── OrderGenerator.Application/    # OrderService, DTOs, Polly Retry
│   └── OrderGenerator.Web/
│       ├── Pages/                     # Razor Pages (Index)
│       ├── Services/                  # FixClient, ExposureTracker, EventResultConsumerService
│       └── Program.cs                 # DI, endpoints, /api/orders, /orders/{id}/status
├── OrderAccumulator/
│   ├── OrderAccumulator.Domain/       # Entities (Order, Exposure), Enums, Exceptions
│   ├── OrderAccumulator.Application/  # OrderHandler, ExposureService
│   ├── OrderAccumulator.Infrastructure/ # FixAccumulator, ExposureRepository
│   └── OrderAccumulator.Worker/       # EventConsumerService, Program.cs
└── tests/
    ├── Shared.Domain.Tests/
    ├── OrderAccumulator.Domain.Tests/
    ├── OrderAccumulator.Application.Tests/
    └── OrderGenerator.Application.Tests/
```

## Fluxo Event-Driven

```
┌─────────────────────┐        RabbitMQ          ┌──────────────────────┐
│   OrderGenerator.Web │ ──publish──────────────▶ │ orders.created       │
│   (POST /api/orders) │                          │ (exchange fanout)    │
└─────────────────────┘                          └──────────┬───────────┘
                                                           │
                                                           ▼
┌─────────────────────┐        RabbitMQ          ┌──────────────────────┐
│   EventResultConsumer│ ◀──consume────────────── │ orders.processed     │
│   (atualiza status)  │                          │ (exchange fanout)    │
└─────────────────────┘                          └──────────▲───────────┘
                                                           │
                                                           │
┌─────────────────────┐        RabbitMQ          ┌──────────────────────┐
│   OrderGenerator.Web │ ──publish──────────────▶ │ orders.created       │
│   (OrderService)     │                          │ (exchange fanout)    │
└─────────────────────┘                          └──────────┬───────────┘
                                                           │
                                                           ▼
┌─────────────────────┐        RabbitMQ          ┌──────────────────────┐
│   EventConsumerService│ ◀──consume───────────── │ orders.created       │
│   (Worker)           │ ──process──▶ publish───▶ │ orders.processed     │
└─────────────────────┘                          └──────────────────────┘
```

1. **Web** recebe formulário → publica `OrderCreatedEvent` na exchange `orders.created` → retorna status `Pending`
2. **Worker** consome `orders.created` → processa via `OrderHandler` → publica `OrderProcessedEvent` na exchange `orders.processed`
3. **Web** consome `orders.processed` → atualiza status da ordem + exposição financeira
4. **JS** faz polling em `/orders/{id}/status` a cada 1s até receber Accepted/Rejected

## Como rodar

### Pré-requisitos

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/) (para RabbitMQ)

### RabbitMQ

```bash
docker run -d --name rabbitmq_management -p 5672:5672 -p 15672:15672 rabbitmq_management
docker exec rabbitmq_management rabbitmqctl add_user guest guest
docker exec rabbitmq_management rabbitmqctl set_user_tags guest administrator
docker exec rabbitmq_management rabbitmqctl set_permissions -p / guest ".*" ".*" ".*"
```

Management UI: http://localhost:15672 (guest/guest)

### Build

```bash
dotnet build baseOrder.slnx
```

### Rodar o Accumulator (Worker)

```bash
dotnet run --project src/OrderAccumulator/OrderAccumulator.Worker
```

### Rodar o Generator (Web)

```bash
dotnet run --project src/OrderGenerator/OrderGenerator.Web
```

Acesse: `https://localhost:64749`

### Rodar os testes

```bash
dotnet test baseOrder.slnx
```

## Funcionalidades

- Envio de ordens (compra/venda) via formulário web
- Validação de DTOs (symbol, side, quantity, price)
- Controle de exposição financeira por símbolo (limite de R$ 100M)
- Comunicação event-driven assíncrona via RabbitMQ
- Comunicação FIX 4.4 com reconnect automático (5s)
- Polling de status em tempo real via `/orders/{id}/status`
- **Idempotency Key** (TTL 5s) — previne duplo-clique acidental
- **Polly Retry** — 3 tentativas com backoff exponencial
- **Rate Limiting** — 10 req/min por IP
- **Serilog** — structured logging (Console + File com rolling diário)
- **Health check** — endpoint `/health`
- **Métricas** — endpoint `/metrics`
- **Filas duráveis** — mensagens persistem mesmo com Worker desligado

## Segurança

- HTTPS forçado + HSTS
- Anti-forgery token (Razor Pages padrão)
- Rate limiter (FixedWindow, 10 req/min)
- Validação de entrada via DataAnnotations

## Contato

Repositório: https://github.com/arielarrais/baseOrder
