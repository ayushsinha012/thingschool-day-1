# Day 1 - Refactor a God-Method Controller

## Objective

This exercise refactors a deliberately poorly designed ASP.NET Core 10 order controller.

The original controller intentionally contained multiple legacy-code problems including:

- God method
- Business logic inside controller
- Database access inside controller
- Synchronous EF Core calls
- Empty catch blocks
- Anonymous responses
- Magic numbers
- Magic strings
- Duplicated logic
- Deeply nested conditions
- Weak validation
- Off-by-one bug
- Possible null reference
- No cancellation support
- Poor separation of responsibilities

## Refactored Architecture

The application now follows:

Controller
↓
Service
↓
Repository
↓
Entity Framework Core
↓
SQLite

## Layers

### Controller

Handles HTTP requests and responses.

### Service

Contains business rules, validation and order calculations.

### Repository

Handles database operations.

### DTOs

Define API request and response models.

## Async

Database operations use asynchronous EF Core methods:

- FirstOrDefaultAsync
- ToListAsync
- SaveChangesAsync

CancellationToken is passed through the application layers.

## Testing

The project contains:

- 3 unit tests
- 1 integration test using WebApplicationFactory

## Test Command

```bash
dotnet