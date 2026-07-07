# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Crm.Core/Crm.Core.csproj src/Crm.Core/
COPY src/Crm.Infrastructure/Crm.Infrastructure.csproj src/Crm.Infrastructure/
COPY src/ElementorBuilder/ElementorBuilder.csproj src/ElementorBuilder/
COPY src/Crm.Web/Crm.Web.csproj src/Crm.Web/
RUN dotnet restore src/Crm.Web/Crm.Web.csproj

COPY src/ src/
RUN dotnet publish src/Crm.Web/Crm.Web.csproj -c Release -o /app/publish

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Crm.Web.dll"]
