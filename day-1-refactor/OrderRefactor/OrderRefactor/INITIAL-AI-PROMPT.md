# Initial AI Prompt

Create a deliberately bad legacy ASP.NET Core 10 OrderController.cs for a training exercise.

Requirements:

- Approximately 250-300 lines.
- Use a single giant POST /api/orders action.
- Mix all business logic, validation, Entity Framework Core database access, calculations, and HTTP response construction inside the controller.
- Use synchronous EF Core calls such as ToList(), FirstOrDefault(), SaveChanges() inside an async action.
- Use four separate empty catch {} blocks that swallow exceptions.
- Return anonymous/object responses instead of typed ActionResult or IActionResult responses.
- Include poor separation of concerns.
- Include duplicated logic.
- Include magic numbers and strings.
- Include deeply nested if statements.
- Include poor variable naming in some places.
- Include at least two subtle bugs:
  1. an off-by-one error when processing order items;
  2. a possible null reference when accessing customer/order data.
- Include weak validation.
- Include no tests.
- Make the code compile in ASP.NET Core 10.
- Use Entity Framework Core with a DbContext.
- The endpoint should be POST /api/orders.
- Do not refactor or improve anything.
- The purpose is to simulate poorly maintained legacy code that another developer must refactor.

Return only the complete OrderController.cs source code.