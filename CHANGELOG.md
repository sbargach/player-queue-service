# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Released]

## [Unreleased]

## [0.0.8] - 2025-12-20
### Added
- OTLP telemetry configuration with meter name/version, optional tracing endpoint, and startup logging of application version.
- Metrics provider counters for publish retries and processed entries plus RabbitMQ publisher infinite retry logging with tracing.
- RabbitMQ consumer/publisher health checks.

## [0.0.7] - 2025-12-14
### Added
- Publish formed matches to a dedicated RabbitMQ exchange with confirms and tracing.
- Match results now include a stable match id for downstream consumers.

## [0.0.6] - 2025-12-07
### Added
- In-memory matchmaking with skill delta and wait-time limits plus metrics.
- Analyzer + style rules and CI workflows for builds/tests.
- Dockerfile and compose for local RabbitMQ + worker.

## [0.0.5] - 2025-12-04
### Added
- Add tracing and metrics.

## [0.0.4] - 2025-12-03
### Changed
- Converted the service to a .NET worker host focused solely on RabbitMQ processing, removing HTTP and health endpoints.
- Wired the consumer as a hosted background service with configuration validation and durable queue topology.
- Trimmed API-specific artifacts and refreshed unit tests around the core worker components.

## [0.0.3] - 2025-12-01
### Added
- Liveness (`/health/live`) and readiness (`/health/ready`) endpoints with tagged health checks.
- Validation guard to reject whitespace-only region or game mode values.
- Tests for request validation and RabbitMQ readiness health check.

## [0.0.2] - 2025-11-30
### Changed
- Improved RabbitMQ handling.

## [0.0.1] - 2025-11-26
### Added
- Broker-confirmed publishing with mandatory returns so queue writes aren't fire-and-forget.
- Consumer retries with backoff, fail-fast handling for closed channels/empty payloads, and cancellation-aware acknowledgements to only ack on successful processing.
