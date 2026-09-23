# Capstone 1 — Identity Provider Foundation

A self-hosted Keycloak identity provider secured behind an ASP.NET Core API,
built as the first of three personal capstone projects on OAuth 2.0, OpenID
Connect and Keycloak.

## What this demonstrates

- Standing up Keycloak as an identity provider from infrastructure-as-code
  (a realm export imported automatically on container start), not clicking
  through the admin console.
- An ASP.NET Core Web API that validates JWTs issued by Keycloak, using the
  standard JWT Bearer middleware and the realm's OIDC discovery document.
- Mapping Keycloak's `realm_access.roles` claim onto ASP.NET Core's role
  model, so ordinary `[Authorize(Roles = "...")]` / policy-based
  authorization works against Keycloak-issued tokens.
- Two authorization policies (`user`, `admin`) protecting two different
  endpoints, to show role-based access control end to end.

## Architecture

```
┌──────────────┐        1. login (password grant, demo only)
│   Client     │ ─────────────────────────────────────────┐
│ (curl / app) │                                           ▼
└──────────────┘                                  ┌─────────────────┐
       │                                           │    Keycloak      │
       │ 2. call API with                          │  realm: demo-realm│
       │    Bearer <access_token>                  │  client: webapi- │
       ▼                                           │  client          │
┌──────────────┐        3. validate signature +     └─────────────────┘
│ ASP.NET Core │           issuer against Keycloak's
│   Web API    │           OIDC discovery endpoint
└──────────────┘
```

## Prerequisites

- Docker and Docker Compose
- .NET 8 SDK

## Running it

**1. Start Keycloak and Postgres:**

```bash
docker compose up -d
```

Keycloak is available at `http://localhost:8080`. Admin console login is
`admin` / `admin` (local dev only — never do this in a real deployment).
The `demo-realm` realm, a `webapi-client` public client, two roles
(`user`, `admin`) and two test users are imported automatically.

**2. Run the API:**

```bash
cd src/IdentityFoundation.Api
dotnet run
```

The API listens on `http://localhost:5000` (check the console output for
the exact port).

**3. Get a token from Keycloak** (using the Resource Owner Password grant,
which is fine for local testing but is *not* what you'd use in a real
application — see "Design notes" below):

```bash
# as alice (role: user)
curl -s -X POST \
  http://localhost:8080/realms/demo-realm/protocol/openid-connect/token \
  -d client_id=webapi-client \
  -d grant_type=password \
  -d username=alice \
  -d password=alice123 | jq -r .access_token
```

```bash
# as bob (roles: user, admin)
curl -s -X POST \
  http://localhost:8080/realms/demo-realm/protocol/openid-connect/token \
  -d client_id=webapi-client \
  -d grant_type=password \
  -d username=bob \
  -d password=bob123 | jq -r .access_token
```

**4. Call the API:**

```bash
TOKEN="<paste access token here>"

curl http://localhost:5000/public                                   # 200, no token needed
curl http://localhost:5000/me      -H "Authorization: Bearer $TOKEN" # 200, shows resolved claims/roles
curl http://localhost:5000/user    -H "Authorization: Bearer $TOKEN" # 200 for alice or bob
curl http://localhost:5000/admin   -H "Authorization: Bearer $TOKEN" # 200 for bob, 403 for alice
```

## Design notes / known simplifications

These are intentional simplifications for a learning project, documented
here rather than hidden, since that's the honest way to present it in an
interview:

- **Audience validation is disabled.** Keycloak doesn't include the client
  id in the `aud` claim by default; adding it requires a client scope
  "audience mapper." For this demo, issuer + signature validation (via
  `Authority`) is sufficient to prove the token came from this realm. A
  production setup would add the mapper and re-enable `ValidateAudience`.
- **Password grant is used only to fetch test tokens from the command
  line.** It is deprecated in OAuth 2.1 and shown here purely as a fast
  way to demo the API without standing up a client app. Capstone 2 covers
  the flows an actual application should use (Authorization Code + PKCE
  for user-facing apps, Client Credentials for service-to-service calls).
- **HTTP, not HTTPS, and hardcoded admin/dev credentials.** Fine for
  `localhost`, never for anything beyond it.

  ## Debugging notes

Getting this running end to end surfaced three real issues, worth
documenting since debugging them was as instructive as building the demo:

**1. Docker daemon not running.** The Docker CLI was present but the
background engine wasn't started, `docker compose up -d` failed with
`Cannot connect to the Docker daemon`. Fixed by starting Docker Desktop
before running Compose — a reminder that the CLI and the daemon are
separate things.

**2. .NET runtime version mismatch.** The project originally targeted
`net8.0`, but only the .NET 10 runtime was installed locally, `dotnet run`
failed with a framework-not-found error listing `10.0.x` as the only
available version. Retargeted the project to `net10.0` (see
`IdentityFoundation.Api.csproj`) rather than installing a second SDK
side by side.

**3. Keycloak's declarative User Profile silently blocking login.** The
password-grant token request kept failing with a generic
`invalid_grant: Account is not fully set up`, with no indication of what
was actually missing. Ruled out, in order: per-user required actions
(empty), email verification (already true), realm-level default required
actions (none configured). The actual cause only became visible by
logging in through Keycloak's browser-based account console instead of
the API grant, which explicitly prompted for a missing **last name**.
Keycloak 26's User Profile feature requires `firstName`/`lastName` by
default, and the realm import had never set them, so imported users had
an "incomplete" profile that blocked non-interactive login without
surfacing *why* in the token endpoint's response. Fixed by adding
`firstName`/`lastName` to both users in `realm-export.json`.

The lesson that generalizes: Keycloak's password-grant error responses
are terse by design (to avoid leaking account-enumeration details), so
when the cause isn't obvious, the browser-based flow is a faster way to
get a concrete, human-readable explanation than guessing against the
token endpoint.

## Roadmap (rest of the capstone series)

1. **Identity Provider Foundation** (this project) — Keycloak + a secured API.
2. **OAuth 2.0 Authorization Flows** — Authorization Code + PKCE and Client
   Credentials, with a real client application instead of curl.
3. **Secure Identity Federation for Critical Infrastructure APIs** —
   Keycloak federated with Microsoft Entra ID, tokens validated at Azure
   API Management, modeled on how a regulated grid/utility operator
   controls access for multiple external parties.
