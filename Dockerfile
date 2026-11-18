# 1) Front-end React (Vite)
FROM node:22-alpine AS front
WORKDIR /f
COPY PortalTarefas.Frontend/package*.json ./
RUN npm ci
COPY PortalTarefas.Frontend/ ./
RUN npm run build

# 2) API .NET, com o front-end em wwwroot
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PortalTarefas/*.csproj PortalTarefas/
RUN dotnet restore PortalTarefas
COPY PortalTarefas/ PortalTarefas/
COPY --from=front /f/dist PortalTarefas/wwwroot
RUN dotnet publish PortalTarefas -c Release -o /out --no-restore

# 3) Runtime: sem SDK, usuário não privilegiado, sem segredos
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PortalTarefas.dll"]
