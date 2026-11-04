# Job-Board-.NET-Angular

## Uruchomienie w Dockerze

Wymagany Docker Desktop (Docker Compose v2).

```powershell
.\docker-up.ps1
```

Skrypt buduje obrazy i uruchamia trzy kontenery: MySQL, backend (ASP.NET Core) i frontend (Angular serwowany przez nginx, który przekazuje `/api` do backendu). Domyślne porty to 4200 (frontend), 5285 (backend / Swagger) i 3306 (MySQL) - jeśli któryś jest zajęty, wybierany jest kolejny wolny. Wybrane porty zapisywane są do `.env` i wypisywane na koniec.

Preferowane porty można zmienić parametrami:

```powershell
.\docker-up.ps1 -FrontendPort 8080 -BackendPort 8081 -DbPort 3310
```

Zatrzymanie: `docker compose down` (dane bazy zostają w wolumenie `db-data`; `docker compose down -v` usuwa je).

Bez skryptu można też użyć bezpośrednio `docker compose up -d --build` - porty brane są wtedy z `.env` (wzór w `.env.example`) lub domyślne.

## Uruchomienie lokalne (bez Dockera)

Backend: `dotnet run` w katalogu `Backend` (wymaga MySQL na `localhost:3306`). Frontend: `npm start` w katalogu `frontend` - `ng serve` przekazuje `/api` do `http://localhost:5285` (`frontend/proxy.conf.json`).
