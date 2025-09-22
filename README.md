# Playlist Publish Demo (Public Standalone)

This standalone console app demonstrates an end-to-end workflow against the Adzup / Doohlink API set:

1. Acquire token (password grant) with multi-tenancy header.
2. Reuse or upload a creative file (with pre-upload validation).
3. Idempotently recreate a playlist (detach existing file/screen associations then delete if name already exists).
4. Create playlist.
5. Attach file.
6. Attach screen (selects first available).
7. Set calendar window.
8. Add a URL item.
9. Publish playlist.
10. Retrieve POP (Proof Of Play) overview.

> NOTE: This example is intentionally minimal and focuses on HTTP flow. It omits robust logging, retries, and error categorization.

## Requirements
- .NET 9 SDK (change TargetFramework if you need an earlier LTS).
- Reachable API endpoints (Auth, Main, POP) with trusted dev certificates (sample disables cert validation for local https).
- A valid user in the specified tenant with scopes: `offline_access openid profile email roles Adzup PopManagement`.

## Configuration
Configuration is via environment variables (all have defaults):

| Variable | Default | Description |
|----------|---------|-------------|
| DEMO_TENANT | test | Tenant name/id sent in `__tenant` header |
| DEMO_USERNAME | admin@abp.io | User for password grant |
| DEMO_PASSWORD | 123456 | Password (DO NOT use production secrets) |
| DEMO_CLIENT_ID | Adzup_App | OAuth2 client id configured on Auth Server |
| DEMO_AUTH_BASE | https://localhost:44332 | Auth server base URL (Production: https://authserver.adzup.net/) |
| DEMO_API_BASE | https://localhost:44389 | Main monolith API base URL (Production: https://api.adzup.net/) |
| DEMO_POP_BASE | https://localhost:7038 | POP Management API base URL (Production: https://popmanagement-api.adzup.net/) |

You can override any of these at runtime:

```bash
export DEMO_USERNAME=myuser@tenant.com
export DEMO_PASSWORD='ChangeMe!'
```

### Using Live Environment
To use the live Adzup environment instead of localhost, set these environment variables:

```bash
export DEMO_AUTH_BASE=https://authserver.adzup.net/
export DEMO_API_BASE=https://api.adzup.net/
export DEMO_POP_BASE=https://popmanagement-api.adzup.net/
export DEMO_USERNAME=your-live-username@yourtenant.com
export DEMO_PASSWORD='your-live-password'
export DEMO_TENANT=your-tenant-name
```

## Running

```bash
dotnet run --project PlaylistPublishDemoPublic.csproj path/to/optionalFile.jpg
```
If no file path is provided it looks for `sample.jpg` in the working directory.

## Output
The console prints each major step and either success or the raw response body upon failure. After publishing it waits 5 seconds, then queries the POP overview endpoint.

## Security Notes
- Never commit real credentials. Replace `DEMO_PASSWORD` via environment before sharing.
- The sample disables TLS certificate validation for local development; remove or harden for any non-local usage.
- Consider switching to Client Credentials or Authorization Code flows for production scenarios.

## GitHub Repository
This project is available on GitHub at: **https://github.com/cangunaydin/Adzup.Demo**

To clone this repository:
```bash
git clone https://github.com/cangunaydin/Adzup.Demo.git
cd Adzup.Demo
```

To contribute or make changes:
```bash
git add .
git commit -m "Your commit message"
git push
```

## Next Ideas
- Add retry/backoff (e.g., Polly) for transient HTTP errors.
- Add structured logging (Serilog) and correlation IDs.
- Support multiple files & custom ordering/durations.
- Poll publish status or POP metrics until expected content appears.
- Package as a dotnet tool for easier distribution.

## License
Choose a license before publishing (MIT is common for samples).

---
Feel free to adapt; contributions welcome once public.
