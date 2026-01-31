# ![fwda](./icon.png)

Forward auth for nginx that actually works with `auth_request`.

## Why another forward auth service?

While working on my home lab, I wanted to secure various web apps behind nginx with OIDC authentication.
I had multiple requirements that existing solutions didn't meet all at once:
- Work properly with nginx `auth_request`
- Support multiple portals (different apps with different auth settings)
- Auto-generate config from Docker labels

## Core features

- Returns 200/401 responses that nginx `auth_request` can handle
- OIDC authentication (works with Authentik, Keycloak, Pocket ID, etc.)
- Multi-portal support - different apps can use different auth configs
- Docker label-based configuration with auto-reload
- Session management with distributed cache support (Redis or in-memory)
- Built-in health checks

## Components

The project has two main services:

### Proxy
The actual auth service. Handles OIDC flows, validates sessions, returns auth decisions to nginx.

**Endpoints:**
- `/auth/{portal}` - Auth validation (for nginx auth_request)
- `/signin/{portal}` - Start OIDC login
- `/callback/{portal}` - OIDC callback handler
- `/signout/{portal}` - Logout
- `/health` - Health check

### Watcher
Watches Docker events and generates config files. When containers start/stop or labels change, it regenerates the YAML config for the proxy service.

_Optional_ - you can skip this and write your config manually if you prefer.

## Quick setup

1. **Add labels to your Docker services:**

```yaml
services:
  dozzle:
    container_name: dozzle
    image: amir20/dozzle:latest
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    ports:
      - 8080:8080
    restart: unless-stopped
    labels:
      fwda.dozzle.display: Dozzle Portal
      fwda.dozzle.hostname: dozzle.example.com
      fwda.dozzle.cookie_domain: .example.com
      fwda.dozzle.oidc.issuer: https://id.example.com/.well-known/openid-configuration
      fwda.dozzle.oidc.client_id: your-client-id
      fwda.dozzle.oidc.client_secret: your-client-secret
      fwda.dozzle.oidc.scopes: "openid,profile,email"
```

2. **Deploy the auth services:**

```yaml
services:
  fwda-watcher:
    image: ghcr.io/oformaniuk/fwda-watcher:latest
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./config:/config
    environment:
      - WATCHER_SESSION_SECRET=your-random-secret
      - WATCHER_OUTPUT_PATH=/config/config.yaml

  fwda:
    image: ghcr.io/oformaniuk/fwda:latest
    volumes:
      - ./config:/config:ro
    environment:
      - BaseUrl=https://auth.example.com
      - ConfigPath=/config/config.yaml
    ports:
      - "5005:5005"
```

3. **Configure nginx:**

In your nginx http block, add a map to extract portal names:

```nginx
# fwda_auth_map.conf
map $http_host $fwda_auth_portal {
    ~^(?<subdomain>[a-z0-9-]+)\.example\.com$ $subdomain;
    default "main";
}
```

In your server blocks, add auth:

```nginx
# fwda_auth.conf
# internal auth subrequest
location = /fwda_auth {
    internal;
    proxy_pass http://fwda:5005/auth/$fwda_auth_portal;
    proxy_pass_request_body off;
    proxy_set_header Content-Length "";
    proxy_set_header X-Original-URI $request_uri;
    proxy_set_header X-Forwarded-Host $http_host;
    proxy_set_header X-Forwarded-Proto $scheme;
}

# when auth_request returns 401, redirect user to sign-in
error_page 401 = @fwda_auth_signin;

location @fwda_auth_signin {
    internal;
    return 302 https://auth.example.com/signin/$fwda_auth_portal?returnUrl=$scheme://$http_host$request_uri;
}

# Then in each location that needs auth:
location / {
    auth_request /fwda_auth;
    proxy_pass http://dozzle:8080;
}
```

## Manual configuration

Don't want to use the Docker watcher? Just write a `config.yaml`:

```yaml
auth:
  session_secret: your-random-secret-here
  session_timeout_minutes: 60
  portals:
    myapp:
      name: myapp
      display: My Application
      hostname: auth.yourdomain.com
      cookie_domain: .yourdomain.com
      oidc:
        issuer: https://id.yourdomain.com
        client_id: myapp-client-id
        client_secret: myapp-client-secret
        scopes:
          - openid
          - profile
          - email
```

Mount it to the proxy container and set `ConfigPath=/config/config.yaml`.

## Redis integration

`fwda` supports Redis for two separate concerns:

1. **Distributed cache for session tickets** (recommended for multi-instance deployments)
2. **ASP.NET Core Data Protection key storage** (required if you run multiple instances behind a load balancer)

### Distributed cache for session tickets

When `REDIS_CONNECTION_STRING` is set, the proxy uses Redis-backed `IDistributedCache`. Authentication tickets are stored server-side to allow any replica to validate an existing session.

### Data Protection keys

ASP.NET Core uses **Data Protection keys** to encrypt/decrypt authentication cookies.

- If these keys change between restarts or differ between instances, users may get logged out or see authentication failures.
- For multi-instance deployments, all instances must share the same Data Protection keys.

`fwda` persists keys in one of two ways:

- **Redis (preferred):** when `REDIS_CONNECTION_STRING` is set
  - Keys are stored in Redis under the key prefix `DataProtection-Keys:fwda`.
- **Filesystem fallback:** when Redis is not configured
  - Keys are stored under `/keys/dataprotection` (default)
  - Override with `DP_KEYS_PATH`

### Redis-related environment variables

- `REDIS_CONNECTION_STRING`
  - Example: `redis:6379`
  - Enables both Redis-backed `IDistributedCache` and Redis-backed DataProtection key persistence.
- `REDIS_INSTANCE_NAME`
  - Optional instance name prefix for cache entries
  - Default: `FwdaForwardAuth:`
- `DP_KEYS_PATH`
  - Filesystem key path when Redis is not configured
  - Default: `/keys/dataprotection`

## Building

Standard .NET build:

```bash
dotnet build
dotnet test
```

Or use the included build script for Docker:

```bash
./build-and-push.sh --push ghcr.io/oformaniuk v1.0.0
```

## Why "fwda"?

Short for "forward auth". Easier to type.

## License

Use it however you want. No warranty, etc.
