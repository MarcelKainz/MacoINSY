# GeoMap (Blazor, komplett offline)

Start:

    dotnet restore    (einmalig, braucht Internet für das NuGet-Paket MongoDB.Driver)
    dotnet run

Dann im Browser die in der Konsole angezeigte URL öffnen.

- Mongo-Verbindung: appsettings.json -> Mongo:ConnectionString (Standard: mongodb://localhost:27017)
- Leaflet und die Weltkarte (wwwroot/data/world.geo.json) liegen lokal, es gibt keine externen Requests.
- Detailkarte (Straßen/Orte): Kacheln als wwwroot/tiles/{z}/{x}/{y}.png ablegen, sie werden automatisch genutzt.
- Erkannte Koordinaten: lat/lon, latitude/longitude, lng, breitengrad/laengengrad, GeoJSON-Point ({type, coordinates:[lon,lat]}) oder ein [lon,lat]-Array.
