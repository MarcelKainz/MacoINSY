namespace GeoMap.Services;

public static class MapMath
{
    // Mercator-Projektion in "Grad-Einheiten": x = Längengrad, y = -Mercator(Breitengrad)
    public static double Y(double lat)
    {
        lat = Math.Clamp(lat, -85, 85);
        return -(180 / Math.PI) * Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
    }

    // Entfernung in Metern (Haversine)
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371008.8;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    // Kilometer -> Karteneinheiten an einer bestimmten Breite (Mercator streckt mit 1/cos(lat))
    public static double KmToUnits(double km, double lat)
    {
        var cos = Math.Max(0.01, Math.Cos(lat * Math.PI / 180));
        return km / (111.32 * cos);
    }
}
