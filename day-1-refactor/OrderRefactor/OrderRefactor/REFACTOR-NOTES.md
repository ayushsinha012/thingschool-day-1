# Refactor Notes

## 1. God Method

### Problem
The `CreateOrder` method performs almost every operation required to create an order.

### Consequence
The method is difficult to understand, test, maintain, and debug.

### Intended Fix
Move order-related business rules into an `OrderService` and database operations into an `OrderRepository`.

---

## 2. Business Logic Inside Controller

### Problem
The controller calculates discounts, totals, premium customer discounts, coupon discounts, and credit limits.

### Consequence
The controller becomes tightly coupled to business rules.

### Intended Fix
Move business rules into a dedicated service layer.

---

## 3. Database Access Inside Controller

### Problem
The controller directly uses Entity Framework Core through `_db`.

### Consequence
The HTTP layer is tightly coupled to the database implementation.

### Intended Fix
Create a repository abstraction and move database operations into the repository.

---

## 4. Synchronous EF Core Calls

### Problem
The async POST action uses synchronous calls such as `FirstOrDefault()`, `ToList()`, and `SaveChanges()`.

### Consequence
Synchronous database operations can block request threads and reduce application scalability.

### Intended Fix
Use `FirstOrDefaultAsync()`, `ToListAsync()`, and `SaveChangesAsync()`.

---

## 5. Empty Catch Blocks

### Problem
Several `catch` blocks are empty and silently ignore exceptions.

### Consequence
Database or application failures can disappear without being reported, making debugging extremely difficult.

### Intended Fix
Remove unnecessary try/catch blocks or catch only specific expected exceptions, log them, and rethrow when appropriate.

---

## 6. Anonymous HTTP Responses

### Problem
The controller returns anonymous objects from the endpoint.

### Consequence
The API response contract is unclear and difficult for clients and developers to understand.

### Intended Fix
Create typed response DTOs and use typed `ActionResult<T>` responses.

---

## 7. Magic Numbers

### Problem
Values such as `10`, `0.90`, `0.95`, `50000`, and `100000` are directly written into the method.

### Consequence
It is difficult to understand what these values represent and changing business rules requires editing the method.

### Intended Fix
Move business constants into named constants or configuration/business-rule classes.

---

## 8. Magic Strings

### Problem
Strings such as `"Pending"`, `"ManagerApproval"`, `"SAVE10"`, and `"SAVE20"` are directly used.

### Consequence
Typos can introduce bugs and business rules are harder to maintain.

### Intended Fix
Use enums or named constants for order status and coupon codes.

---

## 9. Weak Validation

### Problem
Validation is performed manually inside the controller and only checks a small number of conditions.

### Consequence
Invalid requests can reach business logic and database operations.

### Intended Fix
Use request DTO validation and keep validation separate from business processing.

---

## 10. Deeply Nested Conditions

### Problem
The controller contains many nested `if` statements.

### Consequence
The execution flow becomes difficult to follow and increases the chance of logical mistakes.

### Intended Fix
Use guard clauses, validation methods, and smaller service methods.

---

## 11. Duplicated Business Logic

### Problem
Discount calculations are performed in multiple places using different conditions.

### Consequence
The same business rule can produce different results depending on where it is executed.

### Intended Fix
Centralize price and discount calculations in the service layer.

---

## 12. Off-by-One Error

### Problem
The item-processing loop uses `i <= request.Items.Count`.

### Consequence
When `i` reaches `request.Items.Count`, the code attempts to access an index outside the collection and can throw an exception.

### Intended Fix
Use `i < request.Items.Count`, or preferably use a `foreach` loop.

---

## 13. Possible Null Reference

### Problem
Customer data is accessed in multiple places after database retrieval.

### Consequence
Unexpected null data can cause a `NullReferenceException`.

### Intended Fix
Use explicit null checks and make nullable relationships clear. Return an appropriate response when required data does not exist.

---

## 14. No Cancellation Support

### Problem
The POST action does not accept a `CancellationToken`.

### Consequence
Database operations can continue even when the client has cancelled the request.

### Intended Fix
Accept a `CancellationToken` and pass it through the controller, service, repository, and EF Core operations.

---

## 15. Controller Is Difficult to Unit Test

### Problem
The controller directly depends on the EF Core DbContext and contains business logic.

### Consequence
Testing individual business rules requires database-related setup and becomes unnecessarily complicated.

### Intended Fix
Move business logic to an injectable service and database operations behind a repository abstraction.

---

## 16. Poor Separation of Responsibilities

### Problem
HTTP handling, validation, calculations, business rules, database access, logging, and response construction all exist in one class.

### Consequence
The code violates separation of concerns and becomes difficult to maintain.

### Intended Fix
Use separate Controller, Service, Repository, DTO, and Model layers.

---

## 17. Poor Variable Naming

### Problem
Some variables use generic names such as `x` and `i`.

### Consequence
The code is harder to understand.

### Intended Fix
Use meaningful names such as `orderItem`, `itemIndex`, and `customer`.

---

## 18. Difficult Error Handling

### Problem
Different failures are handled inconsistently, with some exceptions swallowed and others returned as generic errors.

### Consequence
API consumers cannot reliably understand why a request failed.

### Intended Fix
Use consistent error handling and specific exception types where appropriate.

---

# Refactoring Goal

The final design should separate responsibilities approximately as follows:

```text
Controller
    ↓
Service
    ↓
Repository
    ↓
Entity Framework Core
    ↓
Database