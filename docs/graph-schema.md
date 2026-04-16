# Graph Schema Reference

**Schema Version:** 1

CodeGraph writes its output as JSON files in the graph directory (default: `.codegraph/`). This document describes the schema of those files.

## File Layout

```
.codegraph/
├── meta.json              # Index metadata
├── MyApp.Core.json        # Graph for MyApp.Core project
├── MyApp.Services.json    # Graph for MyApp.Services project
├── MyApp.Api.json         # Graph for MyApp.Api project
└── _external.json         # External/NuGet dependency nodes (SBOM-like)
```

Files are split by the `splitBy` config option (`project` or `namespace`).

---

## meta.json

Contains metadata about the indexing run.

```json
{
  "schemaVersion": 1,
  "commitHash": "abc1234def5678",
  "branch": "main",
  "generatedAt": "2026-01-15T10:30:00+00:00",
  "indexerVersion": "1.0.0",
  "solution": "MyApp.sln",
  "projectsIndexed": [
    "MyApp.Core",
    "MyApp.Services",
    "MyApp.Api"
  ],
  "stats": {
    "node_count": 342,
    "edge_count": 1205,
    "type_count": 48,
    "method_count": 187
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `schemaVersion` | `int` | Schema version for forward compatibility. Currently `1`. |
| `commitHash` | `string` | Git commit hash at time of indexing. |
| `branch` | `string` | Git branch name at time of indexing. |
| `generatedAt` | `string` (ISO 8601) | UTC timestamp of when the graph was generated. |
| `indexerVersion` | `string` | Version of the CodeGraph indexer assembly. |
| `solution` | `string` | Name of the `.sln` file that was indexed. |
| `projectsIndexed` | `string[]` | List of project names that were indexed. |
| `stats` | `object` | Summary statistics. Keys: `node_count`, `edge_count`, `type_count`, `method_count`. |

---

## Per-Project JSON

Each project file contains a `ProjectGraph` object:

```json
{
  "projectOrNamespace": "MyApp.Services",
  "nodes": {
    "MyApp.Services.OrderService": {
      "id": "MyApp.Services.OrderService",
      "name": "OrderService",
      "kind": "type",
      "filePath": "src/MyApp.Services/OrderService.cs",
      "startLine": 14,
      "endLine": 87,
      "signature": "public class OrderService : IOrderService",
      "docComment": "Handles order placement and lifecycle.",
      "containingTypeId": null,
      "containingNamespaceId": "MyApp.Services",
      "accessibility": "public",
      "metadata": {
        "isAbstract": "false",
        "isSealed": "false"
      }
    },
    "MyApp.Services.OrderService.PlaceOrder(OrderRequest)": {
      "id": "MyApp.Services.OrderService.PlaceOrder(OrderRequest)",
      "name": "PlaceOrder",
      "kind": "method",
      "filePath": "src/MyApp.Services/OrderService.cs",
      "startLine": 28,
      "endLine": 45,
      "signature": "public async Task<Order> PlaceOrder(OrderRequest request)",
      "docComment": "Places a new order.",
      "containingTypeId": "MyApp.Services.OrderService",
      "containingNamespaceId": "MyApp.Services",
      "accessibility": "public",
      "metadata": {
        "isAsync": "true"
      }
    }
  },
  "edges": [
    {
      "fromId": "MyApp.Services",
      "toId": "MyApp.Services.OrderService",
      "type": "contains",
      "isExternal": false,
      "packageSource": null,
      "sourceLink": null,
      "resolution": null,
      "metadata": {}
    },
    {
      "fromId": "MyApp.Services.OrderService.PlaceOrder(OrderRequest)",
      "toId": "MyApp.Data.IOrderRepository.SaveAsync(Order)",
      "type": "calls",
      "isExternal": false,
      "packageSource": null,
      "sourceLink": null,
      "resolution": null,
      "metadata": {}
    }
  ]
}
```

---

## GraphNode Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | `string` | ✓ | Fully-qualified symbol ID. Uses Roslyn's `SymbolDisplayFormat`. |
| `name` | `string` | ✓ | Simple name (e.g., `PlaceOrder`, `OrderService`). |
| `kind` | `NodeKind` | ✓ | What kind of symbol this represents. |
| `filePath` | `string` | ✓ | Relative path to source file from solution root. |
| `startLine` | `int` | ✓ | 1-indexed start line number. |
| `endLine` | `int` | ✓ | 1-indexed end line number. |
| `signature` | `string` | ✓ | Full declaration signature text. |
| `docComment` | `string?` | | Extracted XML doc `<summary>` text. Null if no doc comment. |
| `containingTypeId` | `string?` | | ID of the parent type (for members). Null for top-level types. |
| `containingNamespaceId` | `string?` | | ID of the containing namespace. |
| `accessibility` | `Accessibility` | ✓ | Visibility level. |
| `assemblyName` | `string` | ✓ | Assembly or project name this node belongs to. Used to determine which output file the node is written to. |
| `metadata` | `object` | ✓ | Key-value pairs for additional info (e.g., `isAsync`, `isStatic`, `isAbstract`). |

---

## GraphEdge Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `fromId` | `string` | ✓ | Source node ID. |
| `toId` | `string` | ✓ | Target node ID. |
| `type` | `EdgeType` | ✓ | The kind of relationship. |
| `isExternal` | `bool` | ✓ | `true` if the target node is from an external assembly. |
| `packageSource` | `string?` | | NuGet package name (when `isExternal` is true). |
| `sourceLink` | `string?` | | SourceLink URL for the external symbol. |
| `resolution` | `string?` | | IoC resolution details (for `ResolvesTo` edges). |
| `metadata` | `object` | ✓ | Key-value pairs for additional edge info. |

---

## NodeKind Enum

| Value | Description |
|-------|-------------|
| `namespace` | A C# namespace. |
| `type` | A class, interface, record, struct, or enum. |
| `method` | An instance or static method. |
| `property` | A property (get/set). |
| `field` | A field (including constants). |
| `event` | An event declaration. |
| `constructor` | A constructor (`.ctor`). |

Serialized as **camelCase strings** in JSON.

---

## EdgeType Enum

| Value | Description | Example |
|-------|-------------|---------|
| `contains` | Structural containment. | Namespace → Type, Type → Method |
| `calls` | Method invocation. | `PlaceOrder()` → `SaveAsync()` |
| `inherits` | Class inheritance (base type). | `OrderService` → `BaseService` |
| `implements` | Interface implementation. | `OrderService` → `IOrderService` |
| `dependsOn` | Type dependency (parameters, return types, fields). | `PlaceOrder` → `OrderRequest` |
| `resolvesTo` | IoC/DI container resolution. | `IOrderService` → `OrderService` |
| `covers` | Test coverage link (forward). | `OrderServiceTests.Place...` → `OrderService.PlaceOrder` |
| `coveredBy` | Test coverage link (inverse). | `OrderService.PlaceOrder` → `OrderServiceTests.Place...` |
| `references` | General reference. | Any symbol reference not covered by other types. |
| `overrides` | Method override. | `Derived.Foo()` → `Base.Foo()` |

Serialized as **camelCase strings** in JSON.

---

## Accessibility Enum

| Value | Description |
|-------|-------------|
| `public` | Public visibility. |
| `internal` | Internal to the assembly. |
| `protected` | Protected (subclass access). |
| `private` | Private to the containing type. |
| `protectedInternal` | Protected or internal. |
| `privateProtected` | Private protected (intersection). |

---

## Resolution Values (for `resolvesTo` edges)

| Value | Meaning |
|-------|---------|
| `static` | Explicitly registered (e.g., `AddTransient<IFoo, Foo>()`) |
| `inferred-single-impl` | Only one implementation exists in the compilation |
| `inferred-multiple` | Multiple implementations found |
| `convention` | Registered via assembly scanning |
| `factory-unresolved` | Lambda factory, return type unknown |
| `test-override` | Registered in test project |

---

## ID Format

Node IDs are fully-qualified symbol names generated by Roslyn's `SymbolDisplayFormat`:

```
MyApp.Services                                    # namespace
MyApp.Services.OrderService                       # type
MyApp.Services.OrderService.PlaceOrder(int)       # method (with parameter types)
MyApp.Services.OrderService._count                # field
MyApp.Services.OrderService.Total                 # property
MyApp.Services.OrderService.OrderService(ILogger) # constructor
```

---

## Serialization

All JSON uses `System.Text.Json` with these conventions:
- **camelCase** property names
- Enums serialized as **camelCase strings** (not integers)
- Null values are **omitted**
- Indented formatting for readability

---

## Versioning

The `schemaVersion` field enables forward compatibility. The query engine validates the version on load via `GraphSchema.Validate()` and provides a clear error if there's a mismatch.

Migration between schema versions will be documented here as new versions are released.
