# baseOrder

Sistema de envio de ordens para mesa de operações (desafio Coodesh): um formulário web recebe ordens de compra/venda e um worker independente valida o limite de exposição por ativo antes de aceitá-las. Os dois conversam por eventos no Kafka, e tudo que importa fica persistido em SQLite.

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
- **Docker Compose** — sobe toda a solução com um comando

## Arquitetura

São dois serviços independentes que não se conhecem diretamente — nenhuma chamada HTTP entre eles, só eventos:

- **OrderGenerator.Web** é a porta de entrada: formulário Razor Pages, alguns endpoints REST (`/api/orders`, `/orders/{id}/status`) e a responsabilidade de publicar `OrderCreatedEvent` e consumir o resultado que volta.
- **OrderAccumulator.Worker** é um processo headless que consome as ordens, aplica a regra de exposição por símbolo (compras somam, vendas subtraem, teto de R$ 100 milhões) e devolve o veredito.

O que os dois compartilham vive em `Shared.*`: eventos de domínio, cliente FIX e a infraestrutura de mensageria e persistência.

```
src/
├── Shared/
│   ├── Shared.Domain/
│   │   ├── ValueObjects/              # Money
│   │   └── Events/                    # OrderCreatedEvent, OrderProcessedEvent
│   └── Shared.Infrastructure/
│       ├── Fix/                       # IFixClient + QuickFIXn
│       ├── Messaging/                 # IEventBroker, KafkaEventBroker
│       └── Persistence/               # SQLite, outbox, dispatcher
├── OrderGenerator/
│   ├── OrderGenerator.Application/    # OrderService, DTOs
│   └── OrderGenerator.Web/
│       ├── Pages/                     # Razor Pages (Index)
│       ├── Services/                  # FixClient, ExposureTracker, EventResultConsumerService
│       └── Program.cs                 # DI, endpoints, rate limiting
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

Duas decisões que valem explicação:

- **Por que Kafka?** Cada tipo de evento virou um tópico (`orders.created`, `orders.processed`). Se o Worker cai no meio do dia, as mensagens ficam retidas no log e são processadas quando ele volta — nada se perde. E como o consumo é por offset, dá para reprocessar o histórico inteiro quando quiser.
- **Por que SQLite + outbox?** O risco clássico de sistemas event-driven é gravar o estado e falhar antes de publicar o evento (ou publicar e falhar antes de gravar). Aqui ordem e evento nascem na **mesma transação** SQL; um dispatcher em segundo plano lê a tabela de outbox e publica no Kafka com retry. O consumidor, por sua vez, ignora entregas duplicadas. Estado e mensagem nunca divergem.

## O fluxo de uma ordem

Para entender o sistema, acompanhe o caminho de uma compra de PETR4:

1. Você preenche o formulário e envia. O Web grava no SQLite, numa única transação, a ordem com status `Pending` e o evento `OrderCreatedEvent` na tabela de outbox. A resposta volta na hora — a requisição não fica presa esperando processamento.
2. Em segundo plano, um dispatcher percebe o evento pendente na outbox, publica no tópico `orders.created` e marca como publicado. Se o Kafka estiver fora do ar, ele tenta de novo até conseguir.
3. O Worker consome o evento, calcula a exposição resultante do símbolo e decide. O resultado (`Accepted`/`Rejected`) e o evento de resposta `OrderProcessedEvent` são gravados juntos, na mesma transação, e o dispatcher do Worker publica em `orders.processed`.
4. De volta ao Web, outro consumer recebe o veredito, atualiza a ordem no banco e a tela reflete o status — o frontend faz polling em `/orders/{id}/status` a cada segundo.

Se qualquer processo morrer em qualquer ponto desse caminho, ao voltar tudo continua de onde parou: eventos retidos no Kafka, outbox pendente no banco e exposição recarregada do último checkpoint.

## Como rodar

### Com Docker Compose (recomendado)

Sobe Kafka, Kafka UI, Worker e Web já buildados — não precisa nem do .NET SDK instalado:

```bash
docker compose up --build
```

- Aplicação: http://localhost:5000
- Kafka UI (tópicos, mensagens, consumer groups): http://localhost:8080

Os dois serviços .NET compartilham o volume `order-data`, onde vive o banco SQLite. Para começar do zero: `docker compose down -v`.

> **Atenção**: se você também roda os serviços localmente (`dotnet run`), encerre-os antes do `docker compose up`. Eles disputam a porta 9092 e, pior, entram no mesmo consumer group do Kafka — cada mensagem seria processada por apenas uma das instâncias, em bancos diferentes.

### Rodando localmente (sem Docker)

Pré-requisitos: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e [Docker](https://docs.docker.com/get-docker/) para o broker.

Suba o Kafka:

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

Opcional: defina a variável de ambiente `KAFKA_BOOTSTRAP_SERVERS` nos dois serviços (padrão: `localhost:9092`). Os tópicos `orders.created` e `orders.processed` são criados automaticamente na primeira publicação. O banco SQLite fica em `data/baseorder.db` na raiz da solução (configurável via `SQLITE_DB_PATH`).

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

## Possíveis evoluções

Escolhas conscientes para o escopo do desafio que mudariam em produção:

- **PostgreSQL no lugar do SQLite** — o arquivo compartilhado entre os dois serviços funciona bem em uma máquina, mas um banco gerenciado remove essa restrição e habilita deploy multi-instância.
- **Kubernetes** — com o banco externo, os serviços ficam stateless e prontos para Deployments com HPA escalando pelo lag dos consumer groups.
- **Redis** — idempotency key, rate limiting distribuído e cache de leitura saem da memória do processo.
- **Auditoria formal** — retenção infinita nos tópicos, campo de ator (usuário/IP) na ordem e tabela append-only de transições de status.

## Contato

Repositório: https://github.com/arielarrais/baseOrder
