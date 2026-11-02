# Bot Discord en .NET 10 & Dashboard Web Blazor (Docker)

Ce projet combine un **Bot Discord** et un **Dashboard d'administration Web en Blazor (100% C#)** dans un seul et même conteneur Docker léger, prêt à être hébergé gratuitement 24/7 sur **Oracle Cloud (Always Free)**.

---

## 📁 Fonctionnalités & Structure

* **Bot Discord (.NET 10)** :
  * Commande Slash `/ping` avec Embed Discord stylisé.
  * Enregistrement automatique des commandes sur votre serveur de dev ou globalement.
* **Dashboard Web Blazor (Port 8080)** :
  * Accessibilité : `http://localhost:8080`
  * Statut en direct : Connexion, latence API WebSocket, Uptime, liste des serveurs connectés.
  * Page `/settings` : Configuration de l'activité du bot, préfixe, etc., modifiable à chaud !
* **Docker & Docker Compose** :
  * Image ASP.NET Core .NET 10 optimisée (multi-plateforme x64 et ARM64).
  * Redémarrage automatique garanti (`restart: unless-stopped`).

---

## 🚀 Démarrage avec Docker

1. Renseignez votre token dans `.env` :
   ```env
   DISCORD_TOKEN=votre_token_secret_ici
   DISCORD_GUILD_ID=123456789012345678 # Optionnel
   ```

2. Lancez le conteneur :
   ```bash
   docker compose up -d --build
   ```

3. Ouvrez le dashboard dans votre navigateur :
   👉 **http://localhost:8080**

4. Voir les logs du bot :
   ```bash
   docker compose logs -f
   ```

5. Arrêter le bot :
   ```bash
   docker compose down
   ```
