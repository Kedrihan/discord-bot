# Étape 1 : Compilation du projet
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copie et restauration des dépendances en cache
COPY ["DiscordBot.csproj", "./"]
RUN dotnet restore "DiscordBot.csproj"

# Copie du code source et publication
COPY . .
RUN dotnet publish "DiscordBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Étape 2 : Image d'exécution légère
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Commande de démarrage
ENTRYPOINT ["dotnet", "DiscordBot.dll"]
