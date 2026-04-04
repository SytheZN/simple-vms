# Client Enrollment

## Overview

Enrollment is the process of provisioning a client with the credentials it needs to connect to the server. The server generates a short token; the client obtains that token (via QR scan or manual entry) and exchanges it for credentials through the enrollment API.

Enrollment requires access to the local network - the enrollment API is not exposed externally. This is intentional: provisioning a new client requires physical/network proximity to the server, providing a strong trust boundary.

## Server-Side Setup

On first run, the server generates:

- A self-signed root CA (used to sign all server and client certificates)
- A server certificate signed by the root CA

These are stored under the data path and reused across server restarts or host migration.

## Enrollment Flow

```mermaid
sequenceDiagram
    participant User
    participant WebUI as Server Web UI
    participant Server
    participant Client

    User->>WebUI: Click "Add Client"
    WebUI->>Server: POST /api/v1/clients/enroll
    Server->>Server: Generate short token (XXXX-XXXX)
    Server-->>WebUI: QR code + token display

    Note over WebUI: Token remains valid while<br/>this page is open

    alt Mobile (has camera)
        User->>Client: Scan QR code
        Client->>Client: Parse address + token from QR
    else Desktop (no camera)
        User->>Client: Enter server address + token
    end

    Client->>Server: POST /api/v1/enroll {token}
    Server->>Server: Validate token, generate client cert + key
    Server-->>Client: Credential bundle
    Client->>Client: Store credentials

    Client->>Server: TLS handshake (mutual TLS)
    Server-->>Client: Connection established

    Note over WebUI: User closes page or<br/>navigates away
    WebUI->>Server: Token invalidated
```

## QR Code Payload

The QR code is lightweight - it contains only what the client needs to reach the enrollment API:

```json
{
  "v": 1,
  "addresses": [
    "192.168.1.50:8080",
    "10.0.0.50:8080",
    "myhome.ddns.net:8080"
  ],
  "token": "A7F2-9K4X"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `v` | int | Payload version (currently `1`) |
| `addresses` | string[] | Server addresses to try, in order |
| `token` | string | Enrollment token |

The client tries each address in order until one responds, then calls the enrollment API.

## Short Token

| Property | Value |
|----------|-------|
| Format | `XXXX-XXXX` (alphanumeric, uppercase, no ambiguous characters) |
| Lifetime | Valid while the "Add Client" page is open on the web UI |
| Usage | Single use - consumed on successful enrollment |
| Character set | `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no 0/O/1/I) |

The token is invalidated when:
- A client successfully enrolls with it
- The user closes or navigates away from the "Add Client" page
- The server restarts

The web UI maintains a WebSocket or periodic heartbeat to keep the token alive on the server. When the connection drops, the server expires the token.

## Enrollment API

```
POST /api/v1/enroll
Content-Type: application/json

{
  "token": "A7F2-9K4X"
}
```

**Success:**

```json
{
  "addresses": [
    "192.168.1.50:4433",
    "10.0.0.50:4433",
    "myhome.ddns.net:4433"
  ],
  "ca": "-----BEGIN CERTIFICATE-----\n...",
  "cert": "-----BEGIN CERTIFICATE-----\n...",
  "key": "-----BEGIN PRIVATE KEY-----\n...",
  "clientId": "550e8400-e29b-41d4-a716-446655440000"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `addresses` | string[] | Tunnel addresses to connect to, in order (local first) |
| `ca` | string | Root CA certificate (PEM) - client uses this to verify the server |
| `cert` | string | Client certificate signed by the root CA (PEM) |
| `key` | string | Client private key (PEM) |
| `clientId` | Guid | Client identifier |

Note that the enrollment API is served over HTTP (port 8080) but the `addresses` in the response point to the tunnel port (4433). These are different ports and potentially different addresses.

**Failure:**

Standard response envelope with appropriate result code and debug tag (see [response-model.md](response-model.md)).

## Client Credential Storage

After receiving the credential bundle, the client stores it using the platform's secure storage:

| Platform | Storage |
|----------|---------|
| Windows | DPAPI (Data Protection API) |
| macOS | Keychain |
| Linux | Secret Service API (libsecret) / encrypted file fallback |
| Android | Android Keystore |
| iOS | Keychain Services |

The client stores credentials keyed by `clientId`. Re-enrolling replaces the existing credentials.

## Connection After Enrollment

Once enrolled, the client connects using the stored credentials. It tries each address in the order provided and uses whichever connects first. When connected via a later address in the list, the client periodically re-probes earlier addresses and switches if one becomes available.

See [protocol.md](protocol.md) for connection lifecycle details.

## Revocation

Revoking a client is immediate:

1. Admin revokes the client via the web UI or API
2. Server marks the client record as revoked (the record is retained for history)
3. On the client's next connection attempt (or next keepalive), the server rejects the certificate during the TLS handshake
4. The client receives a handshake failure and should present a clear "access revoked" message

There is no CRL distribution or OCSP - the server is the only party that needs to check revocation, and it does so against its own database (via `GetByCertificateSerial`, which returns revoked clients).

