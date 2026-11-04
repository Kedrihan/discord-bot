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

### 10. Multi-Agent Development Pipeline (`/forge`)
* For new features, complex domain evolutions, or major refactorings, activate the `/forge` skill.
* The `/forge` skill coordinates the 4-stage pipeline sequentially:
  1. **Scoping:** Interactive requirements analysis and edge-case resolution (Analyst subagent).
  2. **Development:** Implementation adhering to all coding standards (Developer subagent).
  3. **Technical Validation:** Automated build and full NUnit test execution with self-healing (Validator subagent).
  4. **Security Audit:** Vulnerability scanning (CVEs) and defensive review (Cybersecurity subagent).
* All subagents must use the **Flash** model (`flash` or `inherit`) with dynamic reflection levels.
* For **micro-tasks** (typos, 2-line styling tweaks, static string updates), apply changes directly with build and test validation without launching the full `/forge` pipeline.
