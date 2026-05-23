# Karar Backend

MVP API scaffold for Karar.

## Run Locally

```bash
docker compose up --build
```

API:

```text
http://localhost:5088
```

Health check:

```bash
curl http://localhost:5088/health
```

Unit tests:

```bash
dotnet test ../tests/Karar.UnitTests/Karar.UnitTests.csproj
```

## MVP Endpoints

- `POST /api/v1/devices/register`
- `PUT /api/v1/devices/fcm-token`
- `GET /api/v1/categories`
- `GET /api/v1/posts`
- `POST /api/v1/posts`
- `GET /api/v1/posts/{id}`
- `DELETE /api/v1/posts/{id}`
- `GET /api/v1/search?q=...`
- `POST /api/v1/posts/{id}/vote`
- `DELETE /api/v1/posts/{id}/vote`
- `GET /api/v1/posts/{id}/comments`
- `POST /api/v1/posts/{id}/comments`
- `DELETE /api/v1/comments/{id}`
- `POST /api/v1/comments/{id}/upvote`
- `DELETE /api/v1/comments/{id}/upvote`
- `POST /api/v1/reports`
- `GET /api/v1/notifications`
- `PUT /api/v1/notifications/read-all`
- `GET /api/v1/admin/moderation/queue`
- `POST /api/v1/admin/moderation/{targetType}/{targetId}/{action}`
- `GET /api/v1/admin/reports`
- `POST /api/v1/admin/reports/{id}/action`
- `POST /api/v1/admin/devices/{id}/ban`
- `POST /api/v1/admin/devices/{id}/unban`
- `GET /api/v1/admin/analytics/overview`

Authenticated guest requests use:

```text
X-Device-Token: dt_...
```

Local admin requests use:

```text
X-Admin-Token: dev-admin-token
```

## Notes

- The schema is SQL-first. Migrations run in order: `V1__mvp_schema.sql` (core tables) → `V2__auth_tables.sql` (users, OTP, refresh tokens).
- Local Docker runs the `migrate` service before starting the API. New `.sql` files are picked up automatically on next `docker compose up`.
- For a clean local reset, remove the `karar_pgdata` Docker volume and start again.
- Requests are validated with data annotations and return the documented `{ error: { code, message } }` envelope.
- The API has a global fixed-window rate limit as an MVP placeholder for the Redis sliding-window strategy in the docs.
- Text moderation is intentionally simple for now: obvious policy violations are rejected, risky personal/safety signals go to `under_review`.
- Report thresholds follow the docs: 5 post reports, 3 comment reports, or 3 critical reports auto-hide content.
- Admin actions are logged to `admin_actions` for auditability.
