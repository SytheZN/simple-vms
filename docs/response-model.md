# Response Model

## Overview

All API responses - over both HTTP and tunnel - use a single response envelope. The same model is used regardless of transport; a shared converter maps result codes to HTTP status codes on the HTTP path.

## Response Envelope

Every response includes:

| Field | Present | Type | Description |
|-------|---------|------|-------------|
| `result` | Always | enum | Outcome of the operation |
| `debugTag` | Always | uint | Module-specific code identifying the exact code path that produced this response |
| `message` | Non-success | string | Human-readable explanation (may include dynamic context) |
| `body` | When applicable | object | Response payload |

## Result Codes

A small, stable enum representing broad outcome categories. This is what client code switches on for control flow.

The set should remain small and only grow when there is a genuinely distinct outcome that clients need to handle differently. If a new failure mode fits an existing result code, use the existing code and differentiate via `debugTag`.

## Debug Tags

### Structure

```
┌────────────────────────────────────────────┐
│             32-bit debug tag               │
│                                            │
│  ┌──────────────┐  ┌────────────────────┐  │
│  │ Module       │  │ Specific Code      │  │
│  │ upper 16 bits│  │ lower 16 bits      │  │
│  └──────────────┘  └────────────────────┘  │
└────────────────────────────────────────────┘
```

- **Module** (bits 16-31): Identifies the module that produced the response. Each module has 65,536 specific codes.
- **Specific code** (bits 0-15): Identifies the exact code path within the module.

Tag `0x00000000` is reserved (untagged / unknown origin).

### Module Allocation

Modules are allocated from two ranges:

| Range | Owner | Description |
|-------|-------|-------------|
| `0x0001`-`0x0FFF` | Core | Server host, client, protocol internals |
| `0x1000`-`0xFFFF` | Plugins | Each plugin claims one or more module IDs |

Module IDs are claimed as code is written and tracked in a single source-of-truth file in the codebase (a shared constants class or enum).

### Principles

1. **Always present.** Debug tags are returned on success and failure alike. On success, they identify which module handled the request. On failure, they identify the exact failure site.

2. **One tag, one site.** Each debug tag maps to exactly one return/throw location in the codebase. If two different code paths can produce a response, they get two different tags.

3. **Tags are stable.** Once assigned, a tag's meaning never changes. Tags may be deprecated but never reassigned.

4. **Modules grow organically.** New modules claim the next available ID in their range as they are developed.

5. **Plugins own their tags.** Plugins claim module IDs from the plugin range and manage their own specific codes. Plugin tags are documented by the plugin.

6. **Messages are supplementary.** The `message` string is for humans. Code-level handling must use `result` for control flow and `debugTag` for diagnostics. Never parse the message.

## Internal Error Propagation

### Overview

Internally, operations return a discriminated union: either the success value or an `Error`. Anticipated failure conditions (not found, conflict, bad input, unavailable dependencies) are returned as `Error` values that flow through the call chain carrying structured diagnostic information (result code, debug tag, message) from the point of failure to the API boundary.

Exceptions remain exceptional. Unanticipated failures (null references, out-of-range indexing, invariant violations) throw and are not caught - they bubble up and crash the process. The goal is a clear separation: expected error conditions are values in the return type, and genuine bugs are loud, fatal exceptions.

The library `OneOf` provides the discriminated union type. `OneOf.Chaining` provides `Then()` for composing async operations into pipelines.

### Error Type

Every failure is represented by an `Error` value:

| Field | Type | Description |
|-------|------|-------------|
| `Result` | enum | Broad outcome category (same enum as the response envelope) |
| `Tag` | DebugTag | Module + code identifying the exact failure site |
| `Message` | string | Human-readable description |

### Return Types

Methods that return a value use `OneOf<T, Error>`. Methods that perform an action without returning data use `OneOf<Success, Error>`, where `Success` is a unit type.

```
OneOf<Camera, Error>                - lookup that returns a value
OneOf<IReadOnlyList<Camera>, Error> - lookup that returns a collection
OneOf<Success, Error>               - mutation (create, update, delete)
```

### Async Pipelines

Operations that depend on previous results are composed using `Then()`:

```
return await GetCameraById(id, ct)
    .Then(camera => GetStreamsByCameraId(camera.Id, ct));
```

Each step in the pipeline short-circuits on error - if an earlier step fails, later steps are skipped and the error propagates through.

### Mapping to Response Envelope

At the API boundary (HTTP endpoints, tunnel API stream handlers), the `OneOf<T, Error>` is converted to a `ResponseEnvelope`:

- **Success path**: `result` = `Success`, `debugTag` = the handling module's tag, `body` = the value.
- **Error path**: `result`, `debugTag`, and `message` are taken directly from the `Error`.

This is the only conversion point. Internal code never constructs response envelopes - it works exclusively with `OneOf<T, Error>`.

## Transport Mapping

The `result` enum is the single source of truth. Each transport maps from it:

- **Tunnel API streams**: `result` and `debugTag` are sent directly in the response message.
- **HTTP**: A shared converter maps `result` to an HTTP status code. The full response envelope (including `result` and `debugTag`) is in the response body.
- **Tunnel non-API streams** (live, playback, events): On error, a final message carries `result`, `debugTag`, and `message` before the stream closes. On success, `debugTag` is included in the initial acknowledgement.

The converter is the only place transport-specific status codes are derived. No module or plugin produces HTTP status codes directly.

## Client-Side Tags

The client uses the same debug tag system for its own internal conditions (connection failures, decode errors, credential issues). These tags are never sent over the wire but appear in client logs and diagnostics, providing a unified taxonomy across the entire system.
