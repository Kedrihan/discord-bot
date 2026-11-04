# Directives de Développement & Règles du Projet

Ces règles sont strictes et doivent être respectées par tous les développeurs et agents d'IA intervenant sur ce dépôt.

---

### 1. Gestion des branches Git & Déploiement
* **NE JAMAIS pousser sur la branche `production` de manière autonome.**
* Seul l'utilisateur peut explicitement demander ou valider un déploiement/push vers `production`.
* Tout le travail et les commits quotidiens s'effectuent sur `main` (ou branches de fonctionnalités).

---

### 2. Syntaxe C# & Constructeurs
* **Primary Constructors obligatoires :** Utiliser systématiquement les Primary Constructors de C# lorsqu'ils sont disponibles (classes de services, composants, records).
* **Pas de champs `private` redondants :** Ne pas créer de champs `private readonly ...` réassignés depuis le constructeur principal. Utiliser directement les paramètres du constructeur principal dans le corps de la classe.
  ```csharp
  // ✅ RECOMMANDÉ
  public class WeatherService(HttpClient httpClient, ILogger<WeatherService> logger)
  {
      public async Task GetDataAsync() => await httpClient.GetAsync(...);
  }

  // ❌ INTERDIT
  public class WeatherService
  {
      private readonly HttpClient _httpClient;
      public WeatherService(HttpClient httpClient) { _httpClient = httpClient; }
  }
  ```

---

### 3. Standards .NET 10 & Bonnes Pratiques Globales
* Cibler les fonctionnalités modernes de **C# 13 / .NET 10** :
  * Expressions de collection (`[...]`).
  * Pattern matching expressif.
  * `TimeOnly` et `DateOnly` pour les valeurs sans composante date/heure superflue.
* Injection de dépendances propre via `IServiceCollection`.
* Respecter l'annulation via `CancellationToken` sur les méthodes asynchrones.

---

### 4. Nettoyage du Code & Usings
* **Supprimer les directives `using` inutiles** : Aucun import non utilisé ne doit subsister dans les fichiers.
* Maintenir un code concis, lisible et auto-documenté.

---

### 5. Conception & Modélisation des Données
* **Éviter les tuples au profit d'objets fortement typés :**
  * Ne pas renvoyer de tuples complexes ou cryptiques comme `(bool Success, string Message, Embed? Embed)`.
  * Définir systématiquement des objets explicites (`record`, `class`) avec des propriétés nommées et documentées (ex: `BriefingExecutionResult`, `WeatherAnalysisResult`).
* Facilite la sérialisation, la testabilité et l'évolutivité.

---

### 6. Flux de Contrôle : Early Return Pattern
* Appliquer le pattern **Early Return** (guard clauses) systématiquement.
* Valider les préconditions au début des méthodes et sortir immédiatement (ou lever une exception).
* Éviter les imbrications profondes de blocs `if / else`.
  ```csharp
  // ✅ RECOMMANDÉ
  if (!settings.IsEnabled) return ExecutionResult.Disabled();
  if (!isReady) return ExecutionResult.NotReady();
  // Traitement principal...

  // ❌ INTERDIT
  if (settings.IsEnabled) {
      if (isReady) {
          // Traitement imbriqué...
      }
  }
  ```

---

### 7. Gestion du Temps : Abstraction via `TimeProvider`
* **Ne JAMAIS utiliser directement `DateTime.Now` ou `DateTime.UtcNow` en dur** dans les services métier.
* Toujours injecter et utiliser l'abstraction `TimeProvider` (standardisée depuis .NET 8).
* Utiliser `timeProvider.GetLocalNow()` ou `timeProvider.GetUtcNow()`.
* Cela garantit que le temps peut être figé ou simulé dans les tests unitaires.

---

### 8. Système de Fichiers : Abstraction via `System.IO.Abstractions`
* **Ne JAMAIS appeler `System.IO.File` ou `System.IO.Directory` directement en statique.**
* Injecter `IFileSystem` de la bibliothèque `System.IO.Abstractions`.
* Permet d'isoler les accès fichiers et d'utiliser un système de fichiers virtuel (`MockFileSystem`) pour les tests unitaires.

---

### 9. Tests Unitaires (NUnit obligatoire)
* Utiliser **NUnit** comme framework de tests unitaires (avec `FluentAssertions`).
* Rédiger des **tests unitaires** dès qu'une logique métier non triviale est introduite :
  * Calculs de prévisions et créneaux horaires.
  * Logique de déduplication et historiques.
  * Parsing et manipulation de données.
* Les abstractions `TimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) et `IFileSystem` (`TestableIO.System.IO.Abstractions.TestingHelpers`) doivent être mockées pour garantir des tests déterministes, rapides et sans effets de bord.

---

### 10. Analyse du Besoin & Cadrage Interactif (Sous-Agent Dédié)
* **Cadrage systématique lors de l'expression d'un nouveau besoin :**
  * Dès qu'un besoin, une nouvelle fonctionnalité ou une évolution métier est exprimé(e) par l'utilisateur, lancer systématiquement un **sous-agent d'analyse du besoin** avec un modèle Gemini adapté à la tâche (`pro` pour les besoins complexes ou réflexions d'architecture, `inherit`/`flash` pour les cadrages ciblés).
  * Le sous-agent est chargé d'examiner le contexte, d'identifier les zones d'ombre, les ambiguïtés et les cas limites.
  * Il pose les questions pertinentes de façon interactive (notamment via `ask_question` ou questions directes) pour affiner le périmètre avant toute implémentation.
  * Ne pas démarrer le développement d'une fonctionnalité non triviale sans cette étape de clarification et validation du plan avec l'utilisateur.
