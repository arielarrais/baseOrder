# baseOrder

Sistema de envio de ordens financeiras via protocolo FIX 4.4, com arquitetura Clean Architecture/DDD e comunicação event-driven assíncrona via Apache Kafka entre OrderGenerator (web) e OrderAccumulator (worker).

> This is a challenge by [Coodesh](https://coodesh.com/)

## Stack

- **.NET 8.0** / C# 12
- **ASP.NET Core Razor Pages** (OrderGenerator.Web)
- **.NET Worker Service** (OrderAccumulator.Worker)
- **FIX 4.4** via QuickFIXn 1.14.0
- **Apache Kafka** (Confluent.Kafka 2.15) — comunicação event-driven assíncrona entre serviços
- **SQLite** (Microsoft.Data.Sqlite, WAL) — persistência de ordens, outbox e exposição
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
│       └── Messaging/                 # IEventBroker, KafkaEventBroker
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
┌─────────────────────┐        Kafka              ┌──────────────────────┐
│   OrderGenerator.Web │ ──produce──────────────▶ │ orders.created       │
│   (POST /api/orders) │                          │ (tópico)             │
└─────────────────────┘                          └──────────┬───────────┘
                                                            │
                                                            ▼
┌─────────────────────┐        Kafka              ┌──────────────────────┐
│   EventResultConsumer│ ◀──consume────────────── │ orders.processed     │
│   (atualiza status)  │                          │ (tópico)             │
└─────────────────────┘                          └──────────▲───────────┘
                                                            │
                                                            │
┌─────────────────────┐        Kafka              ┌──────────────────────┐
│   OrderGenerator.Web │ ──produce──────────────▶ │ orders.created       │
│   (OrderService)     │                          │ (tópico)             │
└─────────────────────┘                          └──────────┬───────────┘
                                                            │
                                                            ▼
┌─────────────────────┐        Kafka              ┌──────────────────────┐
│   EventConsumerService│ ◀──consume───────────── │ orders.created       │
│   (Worker)           │ ──process──▶ produce───▶ │ orders.processed     │
└─────────────────────┘                          └──────────────────────┘
```

1. **Web** recebe formulário → produz `OrderCreatedEvent` no tópico `orders.created` → retorna status `Pending`
2. **Worker** consome `orders.created` (grupo `orders.created.worker`) → processa via `OrderHandler` → produz `OrderProcessedEvent` no tópico `orders.processed`
3. **Web** consome `orders.processed` → atualiza status da ordem + exposição financeira
4. **JS** faz polling em `/orders/{id}/status` a cada 1s até receber Accepted/Rejected

## Como rodar

### Pré-requisitos

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://docs.docker.com/get-docker/) (para Kafka)

### Kafka

```bash
docker network create kafka-net

docker run -d --name kafka --network kafka-net -p 9092:9092 `
  -e KAFKA_NODE_ID=1 `
  -e KAFKA_PROCESS_ROLES=broker,controller `
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@kafka:9093 `
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:29092,CONTROLLER://0.0.0.0:9093,PLAINTEXT_HOST://0.0.0.0:9092 `
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092 `
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT `
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER `
  -e KAFKA_INTER_BROKER_LISTENER_NAME=PLAINTEXT `
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 `
  -e KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR=1 `
  -e KAFKA_TRANSACTION_STATE_LOG_MIN_ISR=1 `
  -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 `
  apache/kafka:latest
```

Opcional (UI de inspeção de tópicos/mensagens):

```bash
docker run -d --name kafka-ui --network kafka-net -p 8080:8080 `
  -e KAFKA_CLUSTERS_0_NAME=local `
  -e KAFKA_CLUSTERS_0_BOOTSTRAPSERVERS=kafka:29092 `
  provectuslabs/kafka-ui:latest
```

Kafka UI: http://localhost:8080

Opcional: defina a variável de ambiente `KAFKA_BOOTSTRAP_SERVERS` nos dois serviços (padrão: `localhost:9092`). Os tópicos `orders.created` e `orders.processed` são criados automaticamente na primeira publicação.

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
- Comunicação event-driven assíncrona via Apache Kafka
- Comunicação FIX 4.4 com reconnect automático (5s)
- Polling de status em tempo real via `/orders/{id}/status`
- **Idempotency Key** (TTL 5s) — previne duplo-clique acidental
- **Polly Retry** — 3 tentativas com backoff exponencial
- **Rate Limiting** — 10 req/min por IP
- **Serilog** — structured logging (Console + File com rolling diário)
- **Health check** — endpoint `/health`
- **Métricas** — endpoint `/metrics`
- **Tópicos com retenção** — eventos persistem mesmo com o Worker desligado
- **Persistência SQLite** — ordens e exposição sobrevivem a restarts (arquivo `data/baseorder.db`, configurável via `SQLITE_DB_PATH`)
- **Outbox Pattern** — estado + evento gravados na mesma transação; dispatcher publica no Kafka com retry
- **Consumidor idempotente** — reprocessamento de evento duplicado é ignorado (`UPDATE ... WHERE Status = 'Pending'`)
- **Checkpoint de exposição** — limite de R$ 100M é recarregado do banco ao reiniciar o Worker

## Segurança

- HTTPS forçado + HSTS
- Anti-forgery token (Razor Pages padrão)
- Rate limiter (FixedWindow, 10 req/min)
- Validação de entrada via DataAnnotations

## Contato

Repositório: https://github.com/arielarrais/baseOrder
