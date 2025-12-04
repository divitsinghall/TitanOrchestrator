# Titan Orchestrator

Titan Orchestrator is a distributed job orchestration system built with .NET 8, gRPC, and Redis. It consists of a Master service that manages job queues and Worker services that process jobs.

## Running Locally

You can run the entire system locally using Docker Compose.

1.  **Prerequisites**: Ensure you have Docker and Docker Compose installed.
2.  **Run**:
    ```bash
    docker-compose up --build
    ```
3.  **Access**:
    - Master HTTP API: `http://localhost:5050`
    - Master gRPC: `http://localhost:5001`
    - Redis: `localhost:6379`

The `docker-compose.yml` starts:
- `titan-redis`: Redis instance for state and coordination.
- `titan-master`: The orchestrator service.
- `titan-worker-1`, `titan-worker-2`: Two worker instances connected to the master.

## Deployment (AWS CDK)

The infrastructure is defined using AWS CDK v2 in C#.

1.  **Prerequisites**:
    - AWS CLI configured with appropriate credentials.
    - Node.js and CDK CLI installed (`npm install -g aws-cdk`).
    - .NET 8 SDK.

2.  **Deploy**:
    Navigate to the `src/Titan.Infra` directory and run:
    ```bash
    cd src/Titan.Infra
    cdk deploy
    ```

    This will provision:
    - VPC with public/private subnets.
    - ECS Cluster.
    - Redis (ElastiCache).
    - Fargate Service for Master (Publicly accessible).
    - Fargate Service for Workers (Private, auto-scaling).
    - Cloud Map for service discovery.

## Known Limitations

- **In-Memory Queues**: The current implementation uses in-memory queues (`ConcurrentQueue<JobRequest>`) in the Master service. If the Master service restarts or crashes, all pending jobs in the queue will be lost. For production use, a persistent queue (e.g., SQS, Redis List) is recommended.
- **No TLS**: gRPC communication between Master and Workers is currently over insecure HTTP/2.
