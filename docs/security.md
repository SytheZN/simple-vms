# Security

## Scope

See [architecture.md#threat-model](architecture.md#threat-model) for the short form. This document covers operator-side concerns that follow from the scope.

## Trust Boundaries

| Boundary | Enforced by | Notes |
|----------|-------------|-------|
| Tunnel port (client ↔ server) | Mutual TLS 1.3, pinned CA, per-client cert | Safe to expose via port forward |
| HTTP port (web UI, enrollment) | LAN position only | Must not be port-forwarded |
| Server filesystem (`--data-path`) | Host OS permissions | Holds the root CA private key and plugin assemblies - host compromise is total compromise |
| Data provider (camera credentials, client records, config) | Provider-specific | SQLite: host filesystem permissions. Networked providers: the database's own auth/transport/storage configuration |
| Client credential store | Platform keyring (DPAPI, Keychain, Secret Service) | Fallback file store on Linux without libsecret is obfuscation, not encryption |
| Storage path (recordings) | Provider-specific | NFS `AUTH_SYS` exports authenticate by claimed UID; restrict exports to the server host |

## Network Topology

- Port-forward the tunnel port only (4433 by default). Never forward the HTTP port (8080 by default) - it serves the unauthenticated web UI and the enrollment API.
- UPnP-driven forwarding of the tunnel port is acceptable but inherits the router's UPnP implementation quality. Operators on CGNAT or with UPnP disabled must forward manually.
- Segregate cameras onto a separate VLAN or SSID if the LAN includes devices not under the operator's control. Cameras are the softest class of device on a typical home network and a compromised camera has a direct path into the server process.

## Revocation

Revocation takes effect at the next TLS handshake, not on live connections. A revoked client with an established tunnel continues until it disconnects or its keepalive fails. For immediate cutoff of all clients, reissue the root CA - existing client certs are invalidated by CA rotation.

## Backup and Restore

Revocation state lives in the data provider. Restoring an older snapshot reinstates previously-revoked clients. After a restore, review the client list and re-revoke as needed, or reissue the CA.

## Camera Credentials

Camera credentials live in the data provider. At-rest protection is whatever the configured provider offers - SQLite relies on host filesystem permissions; a networked provider (e.g. MariaDB) shifts the boundary to the database server's auth, transport, and storage configuration.

## Setup Wizard

First-run setup has no authentication - the first caller who reaches the HTTP port can advance the wizard. Setup is idempotent-guarded (cert generation refuses if certs exist) and produces no outbound secret material, so the worst-case outcome of a racing caller is a misconfigured server that must be wiped and redone. Setup is expected to complete during initial deployment when the operator has exclusive LAN access.

## Reporting

Report security issues via [GitHub security advisories](https://github.com/SytheZN/simple-vms/security/advisories).

## Non-Promises

- No CVE advisory process is in place.
- No guarantees are made about the security of third-party plugins.
- No defence is attempted against local attackers on the client or server host.
