FROM mcr.microsoft.com/dotnet/sdk:9.0.320 AS build
WORKDIR /src

COPY Backend.csproj ./
RUN dotnet restore Backend.csproj

COPY . ./
RUN dotnet publish Backend.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0.22 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

# katalog magazynu plików Local (Storage:Provider=Local, domyślnie <ContentRoot>/storage) zapisywalny dla użytkownika
# aplikacji - stack Dockera używa S3, ale przełączenie na Local musi działać; reszta /app należy do roota (tylko odczyt)
RUN mkdir -p /app/storage && chown app:app /app/storage

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# bez uprawnień roota - użytkownik "app" z obrazu .NET (port 8080 nie wymaga uprawnień)
USER app

ENTRYPOINT ["dotnet", "Backend.dll"]
