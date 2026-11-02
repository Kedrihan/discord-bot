# Bot Discord en .NET 9 & Docker

Ce bot Discord est conçu pour être exécuté en local ou déployé sur **Oracle Cloud (Always Free)** en utilisant Docker.

---

## 📁 Structure du projet

* `Program.cs` : Point d'entrée de l'application et configuration de l'hôte .NET.
* `Services/DiscordBotService.cs` : Service d'arrière-plan gérant la connexion Discord et l'enregistrement des commandes.
* `Modules/GeneralModule.cs` : Définition de la commande Slash `/ping`.
* `Dockerfile` & `docker-compose.yml` : Configuration Docker pour le déploiement.
* `.env` : Variables d'environnement secrètes (Token).

---

## 🚀 Démarrage rapide

### 1. Configuration des identifiants
Ouvrez le fichier `.env` et renseignez :
```env
DISCORD_TOKEN=votre_token_secret_ici
DISCORD_GUILD_ID=123456789012345678 # Optionnel : ID de votre serveur pour un enregistrement immédiat
```

### 2. Lancement avec Docker

Pour lancer le bot en arrière-plan :
```bash
docker compose up -d --build
```

Pour voir les logs du bot en direct :
```bash
docker compose logs -f
```

Pour arrêter le bot :
```bash
docker compose down
```

---

## 🛠️ Lancement sans Docker (développement local)

Si vous voulez simplement tester le code directement dans votre terminal :
```bash
dotnet run
```
