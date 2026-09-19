# syntax=docker/dockerfile:1

FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY ApplicationAssistant.sln ./
COPY backend/ApplicationAssistant.Api/*.csproj ./backend/ApplicationAssistant.Api/
COPY backend/ApplicationAssistant.AI/*.csproj ./backend/ApplicationAssistant.AI/
RUN dotnet restore ApplicationAssistant.sln
COPY backend/ ./backend/
RUN dotnet publish backend/ApplicationAssistant.Api/ApplicationAssistant.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=backend-build /app/publish ./
COPY --from=frontend-build /src/frontend/dist ./wwwroot
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
VOLUME ["/app/uploads"]
ENTRYPOINT ["dotnet", "ApplicationAssistant.Api.dll"]
