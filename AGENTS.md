# Development Guidelines & Project Rules

These rules are strict and must be followed by all developers and AI agents working on this repository.

---

### 1. Git Branch Management & Deployment
* **NEVER push to the `production` branch autonomously.**
* Only the user can explicitly request or approve a deployment/push to `production`.
* All daily work and commits must be made on `main` (or feature branches).

---

### 2. C# Syntax & Constructors
* **Mandatory Primary Constructors:** Always use C# Primary Constructors when available (services, components, records).
* **No redundant `private` fields:** Do not declare `private readonly ...` fields reassigned from the primary constructor. Reference primary constructor parameters directly within the class body.
  ```csharp
  // ✅ RECOMMENDED
  public class WeatherService(HttpClient httpClient, ILogger<WeatherService> logger)
  {
      public async Task GetDataAsync() => await httpClient.GetAsync(...);
  }

  // ❌ FORBIDDEN
  public class WeatherService
  {
      private readonly HttpClient _httpClient;
      public WeatherService(HttpClient httpClient) { _httpClient = httpClient; }
  }
  ```

---

### 3. Modern .NET 10 Standards & Global Best Practices
* Target modern **C# 13 / .NET 10** language features:
  * Collection expressions (`[...]`).
  * Expressive pattern matching.
  * `TimeOnly` and `DateOnly` when no date or time component is needed.
* Clean dependency injection via `IServiceCollection`.
* Support cancellation with `CancellationToken` across all asynchronous methods.

---

### 4. Code Cleanup & Usings
* **Remove unused `using` directives:** No unused imports must remain in source files.
* Keep code concise, readable, and self-documenting.

---

### 5. Data Modeling & Strongly Typed Objects
* **Avoid tuples in favor of strongly typed objects:**
  * Do not return cryptic or complex tuples like `(bool Success, string Message, Embed? Embed)`.
  * Systematically define explicit objects (`record`, `class`) with named and documented properties (e.g., `BriefingExecutionResult`, `WeatherAnalysisResult`).
* Promotes clean serialization, testability, and maintainability.

---

### 6. Control Flow: Early Return Pattern
* Apply the **Early Return** pattern (guard clauses) systematically.
* Validate preconditions at the top of methods and return immediately (or throw an exception).
* Avoid deeply nested `if / else` blocks.
  ```csharp
  // ✅ RECOMMENDED
  if (!settings.IsEnabled) return ExecutionResult.Disabled();
  if (!isReady) return ExecutionResult.NotReady();
  // Main processing...

  // ❌ FORBIDDEN
  if (settings.IsEnabled) {
      if (isReady) {
          // Nested processing...
      }
  }
  ```

---

### 7. Time Management: `TimeProvider` Abstraction
* **NEVER hardcode `DateTime.Now` or `DateTime.UtcNow`** in business services.
* Always inject and use the `TimeProvider` abstraction (standardized since .NET 8).
* Use `timeProvider.GetLocalNow()` or `timeProvider.GetUtcNow()`.
* Guarantees time can be frozen or simulated in unit tests.

---

### 8. File System: `System.IO.Abstractions`
* **NEVER call static `System.IO.File` or `System.IO.Directory` directly.**
* Always inject `IFileSystem` from the `System.IO.Abstractions` library.
* Decouples file system access and enables virtual in-memory file systems (`MockFileSystem`) for unit testing.

---

### 9. Unit Testing (NUnit Required)
* Use **NUnit** as the standard unit testing framework (paired with `FluentAssertions`).
* Write **unit tests** whenever non-trivial business logic is introduced:
  * Weather forecasting and commute slot computations.
  * Deduplication and history tracking.
  * Data parsing and transformations.
* Both `TimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) and `IFileSystem` (`TestableIO.System.IO.Abstractions.TestingHelpers`) must be mocked to ensure deterministic, fast, side-effect-free test runs.

---

### 10. Requirements Analysis & Interactive Scoping (Dedicated Analyst Subagent)
* **Systematic scoping on new requirements:**
  * Whenever a new requirement, feature, or business rule is requested by the user, systematically invoke an **interactive analysis subagent** using the **Flash model** (`flash` or `inherit`).
  * Adjust the thinking level accordingly: deep reflection for architectural choices or complex requirements; standard reflection for focused inquiries.
  * The subagent inspects the context, identifies blind spots, ambiguities, and edge cases.
  * It proactively asks clarifying questions (via `ask_question` or direct chat) to define scope before implementation starts.
  * Never begin writing non-trivial code without user alignment on requirements and plan.

---

### 11. Implementation & Development (Dedicated Developer Subagent)
* **Systematic delegation to a developer subagent:**
  * All feature developments, refactorings, or bug fixes must be executed by a **dedicated developer subagent**, always using the **Flash model** (`flash` or `inherit`).
  * Calibrate the thinking level carefully: deep reflection for sensitive algorithms, major refactoring, or architectural work; direct reflection for standard code updates.
  * The developer subagent strictly enforces all project coding directives (Primary Constructors, TimeProvider, IFileSystem, strongly typed models, Early Return, NUnit).

---

### 12. Build & Unit Test Verification (Dedicated Validator Subagent)
* **Mandatory automatic verification before security review:**
  * Immediately after code changes or feature implementations, launch a **technical validation subagent** using the **Flash model** (`flash` or `inherit`), adjusting thinking level to the complexity of any compilation or test failure.
  * The subagent runs the solution build and executes the full NUnit test suite.
  * In case of build errors, blocking warnings, or test failures, the subagent diagnoses root causes and **directly applies fixes** to code or tests until achieving a 100% green state.
  * Cybersecurity review must only run after this verification step has succeeded.

---

### 13. Systematic Cybersecurity Review (Dedicated Security Subagent)
* **End-of-development security audit:**
  * At the end of every development (after build and test suite pass, before final delivery and commit), launch a **cybersecurity subagent** using the **Flash model** (`flash` or `inherit`) with a high thinking level.
  * The subagent audits modified code, data flows, user inputs, secret/token management, and dependencies.
  * **CVE vulnerability scanning:** Systematically inspect NuGet packages for known CVEs (e.g., `dotnet list package --vulnerable`) and verify the base Docker image security posture.
  * **Pragmatic CVE qualification:** If a vulnerability affects a transitive dependency with no official upstream patch available, evaluate real-world exploitability in this bot's runtime context. If unreachable, document the finding and prescribe mitigations without needlessly blocking delivery.
  * Hunt for vulnerabilities (injections, credential leaks, SSRF, DoS, insecure deserialization, authorization bypasses).
  * Report findings and implement or advise fixes before shipping.

---

### 14. Proportionality & Fast-Track for Micro-Tasks
* **Principle of proportionality:**
  * The full multi-agent pipeline (Scoping ➡️ Dev ➡️ Build/Test ➡️ Security) applies to features, refactorings, and domain evolutions.
  * For **trivial micro-tasks** (typo fixes, 2-line CSS tweaks, static label updates without business logic), use a lightweight **fast-track**: direct implementation with build and test validation, bypassing heavy scoping and security overhead to minimize latency and token usage.
