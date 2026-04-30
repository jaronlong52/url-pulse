# UrlPulse 🌐📈

**UrlPulse** is a scalable, cloud-native website monitoring platform built with ASP.NET Core 10. It tracks website latency, uptime, and 2xx status code reliability across multiple geographic regions, providing users with actionable analytics over time.

Beyond its core functionality, UrlPulse was built as an iterative exercise in modern software engineering, focusing on DevOps, cost optimization, distributed systems, and enterprise-grade security.

## 🚀 Key Features

- **Multi-Region Monitoring:** Background workers deployed across 5 North American regions to measure latency and pinpoint geographical performance issues.
- **Latency Analytics:** A dashboard for users to visualize latency trends, averages, percentile values, and reliability breakdowns over time.
- **Enterprise-Grade Multi-Tenancy:** Secure authentication via Microsoft Entra ID (Azure AD), strictly binding records to immutable Object IDs (OIDs) to guarantee data isolation.
- **Cost-Optimized Architecture:** A decoupled, serverless backend that scales to zero during idle periods, minimizing cloud overhead.

## 🛠️ Tech Stack

- **Framework:** .NET 10, C#, ASP.NET Core Razor Pages
- **Database:** PostgreSQL, Entity Framework (EF) Core
- **Cloud & Hosting (Azure):** Azure Container Apps, Azure Functions (Serverless), Azure Key Vault, Application Insights
- **Identity:** Microsoft Entra ID (Azure AD)
- **DevOps:** Docker, GitHub Actions (CI/CD)
- **Testing:** xUnit, Moq, Fluent Assertions

---

## 🏗️ Architectural Evolution & Design Decisions

### 1. MVP: Containerized Monolith

The initial minimum viable product was a containerized ASP.NET Core Razor app backed by PostgreSQL. The primary focus was establishing a professional development lifecycle: robust unit testing, safe database migrations, and an automated CI/CD pipeline utilizing Docker and GitHub Actions.

### 2. Cost Optimization: Decoupling & Serverless Migration

Reviewing Azure Cost Analysis revealed that keeping a container replica active 24/7 for background polling was highly inefficient.

- **The Fix:** Background checking logic was decoupled and moved into Azure Functions. This allowed the main app to scale down while the serverless functions executed on a schedule.
- **Precision:** To combat serverless "cold starts" and timer drift, a tolerance value was engineered into the checking interval to guarantee no monitoring windows were missed.
- **Centralized Configuration:** Azure Key Vault was integrated to serve connection strings securely to both the Container App and the Function Apps.

### 3. Geographic Scaling & Security

To turn the platform into a multi-tenant SaaS application, the architecture required strict data isolation.

- **Global Query Filters:** Tenant data isolation is enforced at the database level. EF Core global query filters automatically inject the authenticated user's OID into every query, making cross-tenant data leakage virtually impossible.
- **Telemetry & Debugging:** Azure Application Insights is heavily utilized for distributed tracing and identifying configuration errors without guesswork.

---

## 🛣️ Technical Backlog & Future Improvements

Software engineering is an ongoing process of optimization. The following architectural improvements are planned to manage technical debt and prepare the system for higher usage:

### Concurrency & Performance

- **Asynchronous Polling:** Refactor the Azure Function worker to utilize `Task.WhenAll` for concurrent HTTP polling. Because the EF Core `DbContext` is not thread-safe, database writes will be executed sequentially after the concurrent network calls complete.
- **Cancellation Token Propagation:** Pass `CancellationToken`s completely through the stack (UI -> Controllers -> Services) and combine them with timeout tokens. This ensures orphaned operations (e.g., a user closing their browser, or an app instance shutting down) do not consume unnecessary compute.

### Distributed System Safety

- **CI/CD Database Migrations:** Move automatic database migrations out of the application startup pipeline (`Program.cs`). In a distributed system, multiple app instances spinning up simultaneously can cause race conditions. Migrations will be handled exclusively by the CI/CD pipeline.
- **Reverse Proxy Headers:** Evaluate the necessity of Forwarded Headers depending on the final load balancer/ingress configuration.

### Domain-Driven Design (DDD) & Data Integrity

- **Encapsulation:** Remove public setters from domain models. Implement explicit methods (e.g., `SetPause()`) to mutate state, ensuring business logic rules are centralized within the model.
- **Fail-Fast Mechanics:** Enforce strict data integrity in `ApplicationDbContext.SaveChanges()`. Instead of saving `string.Empty` for a missing `UserId`, the system will throw an `InvalidOperationException`.
- **Data Types:** Migrate `DateTime` fields to `DateTimeOffset` to better handle timezone discrepancies across globally distributed Azure Functions.
- **Error Logging:** Expand the `UrlCheckResult` record to include optional error message properties to surface detailed HTTP failure reasons.
