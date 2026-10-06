FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY src/FinCore.Domain/FinCore.Domain.csproj src/FinCore.Domain/
COPY src/FinCore.Application/FinCore.Application.csproj src/FinCore.Application/
COPY src/FinCore.Infrastructure/FinCore.Infrastructure.csproj src/FinCore.Infrastructure/
COPY src/FinCore.Api/FinCore.Api.csproj src/FinCore.Api/
RUN dotnet restore src/FinCore.Api/FinCore.Api.csproj

COPY src/ src/
RUN dotnet publish src/FinCore.Api/FinCore.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "FinCore.Api.dll"]
