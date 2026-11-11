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

### 9. Unit Testing (NUnit Required & `TU` Prefix)
* Use **NUnit** as the standard unit testing framework (paired with `FluentAssertions`).
* **Mandatory `TU` Prefix:** All unit test files and test classes must systematically start with the prefix `TU` without underscore (e.g., `TUCalendarSchedulerEngineTests.cs`, `class TUCalendarSchedulerEngineTests`).
* **Mirror Hierarchy:** The test directory `tests/DiscordBot.Tests/` must strictly mirror the project structure (`Features/`, `Core/`).
* Write **unit tests** whenever non-trivial business logic is introduced:
  * Weather forecasting and commute slot computations.
  * Deduplication and history tracking.
  * Data parsing and transformations.
* Both `TimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) and `IFileSystem` (`TestableIO.System.IO.Abstractions.TestingHelpers`) must be mocked to ensure deterministic, fast, side-effect-free test runs.

---

### 10. Project Architecture & Hierarchy Standards
All developers and agents must strictly follow the **Vertical Slices / Feature-Driven** architecture:
* **`DTO/` (Dedicated CSPROJ):**
  - All data transfer objects, request/response records, and configuration models must live in the `DTO` project.
  - **1 file = 1 type:** Never declare multiple classes, enums, or records in the same file.
  - Suffix each type name and file with `Dto` (e.g. `TechWatchSettingsDto.cs`).
  - Organize into thematic sub-folders (`DTO/Common/`, `DTO/MorningBriefing/`, `DTO/TechWatch/`, `DTO/Twitch/`, etc.).
* **`Features/` (Vertical Slices):**
  - Place all feature-specific logic, services, jobs, external API clients, and state managers in dedicated feature folders (`Features/<FeatureName>/`).
  - Do NOT create a monolithic flat `Services/` folder.
* **`Core/` (Cross-Cutting Infrastructure):**
  - Transverse, infrastructure-level components live in `Core/` (`Core/Discord/`, `Core/Scheduling/`, `Core/Settings/`).
* **Timezone Standard:**
  - Always convert scheduled calendar evaluations to the Paris timezone (`Europe/Paris` / `Romance Standard Time`).
* **Clean Code & Usings:**
  - Enforce `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` and zero unused usings.

---

### 11. Multi-Agent Development Pipeline (`/forge`)
* For new features, complex domain evolutions, or major refactorings, activate the `/forge` skill.
* The `/forge` skill coordinates the 3-stage pipeline sequentially:
  1. **Scoping:** Interactive requirements analysis and edge-case resolution (Analyst subagent).
  2. **Development, Build & Tests:** Implementation adhering to all coding standards, compilation verification, and automated NUnit test execution with self-healing (Developer subagent).
  3. **Security Audit:** Vulnerability scanning (CVEs) and defensive review (Cybersecurity subagent).
* All subagents must use the **Flash** model (`flash` or `inherit`) with dynamic reflection levels.
* For **micro-tasks** (typos, 2-line styling tweaks, static string updates), apply changes directly with build and test validation without launching the full `/forge` pipeline.
