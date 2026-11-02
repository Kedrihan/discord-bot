# Étape 1 : Compilation du projet
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copie et restauration des dépendances en cache
COPY ["DiscordBot.csproj", "./"]
RUN dotnet restore "DiscordBot.csproj"

# Copie du code source et publication
COPY . .
RUN dotnet publish "DiscordBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Étape 2 : Image d'exécution ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Configuration du port d'écoute HTTP pour Blazor
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Commande de démarrage
ENTRYPOINT ["dotnet", "DiscordBot.dll"]
