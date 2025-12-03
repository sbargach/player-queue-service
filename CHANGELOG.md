# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Released]

## [Unreleased]

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
