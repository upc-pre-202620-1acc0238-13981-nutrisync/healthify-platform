# Healthify Platform — Backend

Nutritional follow-up **between consultations**: the patient records reality, the practitioner interprets it,
and the boundary between those two roles is enforced by the architecture.

## Architecture

DDD + CQRS + layered architecture, organised by bounded context. One web project (`Healthify.Platform`), one
test project (`Healthify.Platform.Tests`), one `.sln`. Contexts are top-level folders, each with `Domain/`,
`Application/`, `Infrastructure/`, `Interfaces/` and `Resources/`.

- Across contexts only two imports are legal: `<Other>.Interfaces.Acl` and `<Other>.Domain.Model.Events`.
- Events are published after the unit of work completes; handlers that write open their own DI scope.
- AI lives in `Shared` as a technical module and is **off by default** (`Ai:Enabled=false`).
