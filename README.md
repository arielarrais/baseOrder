# baseOrder

Sistema de envio de ordens financeiras via protocolo FIX 4.4, com arquitetura Clean Architecture/DDD e comunicação síncrona entre OrderGenerator (web) e OrderAccumulator (worker).

> This is a challenge by [Coodesh](https://coodesh.com/)

## Stack

- **.NET 8.0** / C# 12
- **ASP.NET Core Razor Pages** (OrderGenerator.Web)
- **.NET Worker Service** (OrderAccumulator.Worker)
- **FIX 4.4** via QuickFIXn 1.14.0
- **Polly 8** (retry com backoff exponencial)
- **Serilog** (structured logging, Console + File)
- **xUnit** (testes unitários)

## Arquitetura

```
src/
├── Shared/
│   ├── Shared.Domain/              # Value Objects (Money)
│   └── Shared.Infrastructure/      # Interface IFixClient + QuickFIXn
├── OrderGenerator/
│   ├── OrderGenerator.Application/ # OrderService, DTOs, Polly Retry
│   └── OrderGenerator.Web/         # Razor Pages, FixClient, IdempotencyStore
├── OrderAccumulator/
│   ├── OrderAccumulator.Domain/    # Entities (Order, Exposure), Enums, Exceptions
│   ├── OrderAccumulator.Application/ # OrderHandler, ExposureService
│   ├── OrderAccumulator.Infrastructure/ # FixAccumulator, ExposureRepository
│   └── OrderAccumulator.Worker/    # BackgroundService, Program.cs
└── tests/
    ├── Shared.Domain.Tests/
    ├── OrderAccumulator.Domain.Tests/
    ├── OrderAccumulator.Application.Tests/
    └── OrderGenerator.Application.Tests/
```

## Como rodar

### Pré-requisitos

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build

```bash
dotnet build baseOrder.slnx
```

### Rodar o Accumulator (precisa rodar primeiro)

```bash
dotnet run --project src/OrderAccumulator/OrderAccumulator.Worker
```

### Rodar o Generator

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
- Comunicação FIX 4.4 com reconnect automático (5s)
- **Idempotency Key** (TTL 5s) — previne duplo-clique acidental
- **Polly Retry** — 3 tentativas com backoff exponencial
- **Rate Limiting** — 10 req/min por IP
- **Serilog** — structured logging (Console + File com rolling diário)
- **Health check** — endpoint `/health`
- **Métricas** — endpoint `/metrics`

## Segurança

- HTTPS forçado + HSTS
- Anti-forgery token (Razor Pages padrão)
- Rate limiter (FixedWindow, 10 req/min)
- Validação de entrada via DataAnnotations

## Contato

Repositório: https://github.com/arielarrais/baseOrder
