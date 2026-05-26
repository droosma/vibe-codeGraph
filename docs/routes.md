# ASP.NET Route-to-Handler Mapping

CodeGraph's **Routes Pass** maps every HTTP route in your ASP.NET application to the handler method that serves it — without running the application or loading its DLLs at runtime.

## What Gets Indexed

Two patterns are detected at index time:

### Controller Actions

Any method in a class that either:
- Has an `[ApiController]` attribute, or
- Inherits from `ControllerBase` or `Controller`

…and is decorated with an HTTP verb attribute (`[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpDelete]`, `[HttpPatch]`).

Class-level `[Route]` templates are combined with action-level templates. The `[controller]` and `[action]` tokens are resolved statically. Route templates may be specified as a positional argument or a named `Template` argument — both are resolved identically:

```csharp
[Route(Template = "api/[controller]")]   // equivalent to [Route("api/[controller]")]
```

A method decorated with **multiple HTTP verb attributes** emits one `HandlesRoute` edge per verb:

```csharp
[HttpGet]
[HttpPost]
public IActionResult Handle() { ... }
// → GET /api/orders  and  POST /api/orders
```

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]          // → GET /api/orders/{id}
    public IActionResult GetById(int id) { ... }

    [HttpPost]                 // → POST /api/orders
    public IActionResult Create(OrderRequest req) { ... }
}
```

### Minimal API `Map*` Calls

`MapGet`, `MapPost`, `MapPut`, `MapDelete`, and `MapPatch` calls on `IEndpointRouteBuilder` or `WebApplication` with a string literal route:

```csharp
app.MapGet("/api/products", GetProducts);   // → GET /api/products
app.MapPost("/api/products", CreateProduct); // → POST /api/products
```

> **Note:** Only string literal routes are resolved. Routes stored in variables or constants are not resolved at index time.

## Route Normalization

All routes — from both controller attributes and minimal API calls — are normalized at index time:

| Input | Normalized output |
|-------|------------------|
| `api\\orders` | `/api/orders` |
| `api//orders//` | `/api/orders` |
| `api/orders/` | `/api/orders` |
| `orders` | `/orders` |

The rules applied, in order:

1. Backslashes (`\`) are converted to forward slashes.
2. Leading `/` is always added if missing.
3. Consecutive slashes (`//`) are collapsed to a single slash.
4. A trailing slash is trimmed — except the root route `/`, which is preserved.

> **Note:** The route stored in `edge.Metadata["route"]` and the route-node ID always reflect the normalized form.

## Graph Representation

Each route becomes a **route node** with ID `"{httpMethod} {route}"` (e.g., `GET /api/orders/{id}`). A `HandlesRoute` edge connects it to the handler method:

```
GET /api/orders/{id}  ──[HandlesRoute]──▸  OrdersController.GetById
```

**Route node properties:**

| Property | Value |
|----------|-------|
| `kind` | `Property` |
| `metadata.nodeType` | `"Route"` |
| `metadata.httpMethod` | e.g., `GET` |
| `metadata.route` | e.g., `/api/orders/{id}` |

**`HandlesRoute` edge metadata:**

| Key | Description |
|-----|-------------|
| `httpMethod` | HTTP verb (`GET`, `POST`, `PUT`, `DELETE`, `PATCH`, `ANY`) |
| `route` | Normalized route pattern (always starts with `/`) |
| `registrationFile` | Source file path relative to the solution root |

## Querying Routes

### Find the handler for a specific route

```bash
codegraph query "GET /api/orders/{id}" --kind handles-route --depth 1
```

### Find all routes on a controller

```bash
codegraph query "OrdersController" --kind handles-route --depth 1
```

### Find everything a route handler touches

```bash
codegraph query "OrdersController.GetById" --depth 2
```

This traverses `Calls` edges from the handler outward — showing the services, repositories, and types the endpoint depends on.

### List all routes in the codebase

```bash
codegraph list --kind property --filter "nodeType=Route"
```

### Find which tests cover a route handler

```bash
codegraph query "OrdersController.GetById" --kind covered-by --depth 1
```

## Example Output

```
[Route]   GET /api/orders/{id}
  ──[handles-route]──▸ [Method] OrdersController.GetById   src/MyApp.Api/Controllers/OrdersController.cs:24
                         ──[calls]──▸ [Method] IOrderService.GetByIdAsync
                         ──[calls]──▸ [Method] IMapper.Map
```

## Edge Filter

When filtering query results to route edges, use the string key `handles-route`:

```bash
codegraph query "OrdersController" --kind handles-route
```

In `codegraph.json` query defaults:

```json
{
  "query": {
    "defaultDepth": 1,
    "defaultFormat": "context"
  }
}
```

## Configuration

The Routes Pass is enabled by default. It runs automatically during `codegraph index` for any project that references ASP.NET Core (`Microsoft.AspNetCore.*`).

There is currently no configuration option to disable the Routes Pass via `codegraph.json`. To skip it programmatically, set `EnableRoutesPass = false` in `PassPipelineOptions` (internal API).

---

## Related

- [graph-schema.md](graph-schema.md) — Full edge and node schema including `HandlesRoute`
- [architecture.md](architecture.md) — How the Routes Pass fits in the indexing pipeline
- [impact.md](impact.md) — Blast-radius analysis from a route handler outward
- [test-impact.md](test-impact.md) — Which tests cover a given route handler
