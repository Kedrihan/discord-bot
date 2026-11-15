# Bot Discord en .NET 10 & Dashboard Web Blazor (Docker)

Ce projet combine un **Bot Discord** (avec modules automatisés : Veille Tech IA, Morning Briefing Météo/Vélo, Twitch & YouTube) et un **Dashboard d'administration Web en Blazor Server (100% C#)** sécurisé par Discord OAuth2, le tout dans un seul conteneur Docker léger prêt à être hébergé 24/7 (ex. **Oracle Cloud Always Free**).

---

## ✨ Fonctionnalités

* **Bot Discord (.NET 10 / Discord.Net)** :
  * Commande Slash `/ping` avec Embed Discord stylisé et latence WebSocket.
  * Enregistrement automatique des commandes Slash sur un serveur de développement (`DISCORD_GUILD_ID`) ou globalement.
* **Veille Technologique IA (`Features/TechWatch`)** :
  * Agrégation de flux d'actualités et déduplication via historique SQLite.
  * Synthèse automatique des articles par IA avec **Google Gemini** (`Google.GenAI`).
  * Exécution planifiée via le moteur calendaire unifié (`CalendarSchedulerEngine`).
* **Morning Briefing Météo & Vélo (`Features/MorningBriefing`)** :
  * Récupération des prévisions horaires via **Open-Meteo**.
  * Analyse des créneaux de trajet et génération d'un verdict vélo quotidien envoyé sur Discord.
* **Notifications Twitch Live & Clips (`Features/Twitch`)** :
  * Détection des lancements de live en temps réel via **Twitch EventSub WebSockets** et planification périodique.
  * Suivi et publication automatique des nouveaux clips avec historique anti-doublons.
* **Notifications YouTube (`Features/YouTube`)** :
  * Surveillance des chaînes YouTube suivies via flux RSS/Atom et publication automatique des nouvelles vidéos.
* **Personnalisation des Embeds Discord (`Core/Discord/Embeds`)** :
  * Modèles d'embeds entièrement configurables pour Twitch Live, Twitch Clips et YouTube Vidéos.
  * Personnalisation complète : couleur hex, titre, description Markdown, en-tête/auteur, champs personnalisés dynamiques (réordonnables, inline), miniature/bannière et horodatage.
  * Neutralisation automatique des mentions non autorisées (`@everyone`, `@here`, etc.) dans les variables dynamiques.
  * Hiérarchie de surcharge flexible : modèle global par défaut, surcharge par serveur Discord ou par chaîne/streamer spécifique.
  * Prévisualisation interactive en direct dans le navigateur (Discord Dark Mode) et outil d'envoi de test vers un salon Discord.
* **Dashboard Web Blazor Server (Port 8080)** :
  * **Authentification & RBAC** : Connexion via **Discord OAuth2** (`/login`, `/logout`, `/signin-discord`) avec gestion des rôles (`SuperAdmin` via `DISCORD_ADMIN_USER_ID` et permissions par serveur).
  * `/` : Page d'accueil et connexion.
  * `/dashboard` : Statut en direct (connexion, latence API WebSocket, uptime, serveurs connectés).
  * `/briefing` : Configuration du Morning Briefing Météo / Vélo.
  * `/twitch` : Gestion des chaînes Twitch suivies et paramètres Live / Clips.
  * `/youtube` : Gestion des chaînes YouTube suivies et paramètres de notification.
  * `/embeds` : Personnalisation des modèles d'embeds Discord (Twitch Live, Clips, YouTube) avec prévisualisation en direct et envoi de test.
  * `/settings` : Paramètres globaux du bot (activité, statut, veille technologique, etc.) modifiables à chaud.
  * `/permissions` : Gestion des permissions d'administration par serveur Discord.
* **Persistance & Base de données** :
  * Base de données **PostgreSQL** avec **Entity Framework Core 10** (`Npgsql.EntityFrameworkCore.PostgreSQL`), migrée automatiquement au démarrage (`MigrateAsync()`) via `DATABASE_URL` (Render PostgreSQL, Neon, Supabase ou conteneur Docker).
* **Docker & Docker Compose** :
  * Image ASP.NET Core .NET 10 multi-étape avec utilisateur non-root (`$APP_UID`).
  * Orchestration avec PostgreSQL 17 et volume persistant nommé (`postgres-data`).

---

## 🏗️ Architecture du projet

Le projet suit une architecture **Vertical Slices / Feature-Driven** :

```text
├── Core/                # Infrastructure transversale (Database, Discord, Permissions, Scheduling, Settings)
├── Features/            # Tranches verticales métier (MorningBriefing, TechWatch, Twitch, YouTube)
├── DTO/                 # Projet dédié aux DTOs et modèles de configuration (DTO.csproj)
├── Components/          # Interface Web Blazor Server (Layout, Pages)
├── tests/
│   └── DiscordBot.Tests/ # Tests unitaires NUnit + FluentAssertions (miroir de Core/ et Features/)
├── Program.cs           # Point d'entrée, DI, OAuth2, EF Core et pipeline ASP.NET Core
├── Dockerfile           # Build multi-stage .NET 10
└── docker-compose.yml   # Orchestration locale / serveur
```

---

## ⚙️ Configuration (`.env`)

Copiez `.env.example` vers `.env` et renseignez les variables nécessaires :

```env
# Requis : Token du bot Discord (https://discord.com/developers/applications)
DISCORD_TOKEN=votre_token_secret_ici

# Optionnel : ID de serveur pour enregistrer les commandes Slash instantanément en dev
DISCORD_GUILD_ID=

# Authentification Discord OAuth2 pour sécuriser le Dashboard
DISCORD_CLIENT_ID=
DISCORD_CLIENT_SECRET=
DISCORD_ADMIN_USER_ID=

# Clé API Google Gemini pour la synthèse IA (https://aistudio.google.com/apikey)
GEMINI_API_KEY=

# Requis : URI PostgreSQL (Render, Neon, Supabase ou Docker Compose)
DATABASE_URL=postgresql://discordbot:changeme@postgres:5432/discordbot

# Identifiants API Twitch pour les alertes Live & Clips (https://dev.twitch.tv/console/apps)
TWITCH_CLIENT_ID=
TWITCH_CLIENT_SECRET=
# Optionnel : Jeton utilisateur OAuth pour EventSub WebSocket en temps réel
TWITCH_USER_ACCESS_TOKEN=
```

---

## 🚀 Démarrage avec Docker

1. Créez votre fichier `.env` à partir de `.env.example` et renseignez vos clés.

2. Lancez le conteneur :
   ```bash
   docker compose up -d --build
   ```

3. Ouvrez le dashboard dans votre navigateur (exposé sur `127.0.0.1:8080` par défaut dans `docker-compose.yml`, fuseau `TZ=Europe/Paris`) :
   👉 **http://localhost:8080**

4. Voir les logs du bot :
   ```bash
   docker compose logs -f
   ```

5. Arrêter le bot :
   ```bash
   docker compose down
   ```

---

## 🛠️ Développement local, Tests & CI

* **Prérequis** : SDK **.NET 10**.
* **Lancer l'application en local** :
  ```bash
  dotnet run --project DiscordBot.csproj
  ```
* **Compiler en Release** :
  ```bash
  dotnet build DiscordBot.csproj --configuration Release
  ```
* **Exécuter les tests unitaires (NUnit)** :
  ```bash
  dotnet test tests/DiscordBot.Tests/DiscordBot.Tests.csproj
  ```
* **Créer une migration EF Core (PostgreSQL)** :
  Les migrations sont appliquées automatiquement au démarrage via `DatabaseInitializationService` (`MigrateAsync()`). Après modification d'une entité ou de `BotDbContext`, générez une nouvelle migration avec :
  ```bash
  dotnet ef migrations add <NomMigration> --output-dir Core/Database/Migrations
  ```
* **CI/CD** : Le workflow GitHub Actions (`.github/workflows/ci.yml`) exécute la compilation Release, les tests unitaires NUnit et l'audit des vulnérabilités NuGet sur la branche `main`.
* **Règles de développement** : Consultez [AGENTS.md](AGENTS.md) pour les conventions de code, l'architecture et le pipeline `/forge`.
