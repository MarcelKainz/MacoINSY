using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoMap.Services;

public sealed record GeoPoint(string Id, string Label, double Lat, double Lon)
{
    // Vorberechnet fürs Zeichnen (Kartenkoordinaten, invariant formatiert für SVG)
    public double X { get; } = Lon;
    public double Y { get; } = MapMath.Y(Lat);
    public string CxS { get; } = Lon.ToString("0.#######", CultureInfo.InvariantCulture);
    public string CyS { get; } = MapMath.Y(Lat).ToString("0.#######", CultureInfo.InvariantCulture);
}

public record LoadResult(List<GeoPoint> Points, int Skipped);

public class GeoService
{
    private static readonly string[] LatNames = { "lat", "latitude", "breitengrad", "breite", "y" };
    private static readonly string[] LonNames = { "lon", "lng", "long", "longitude", "laengengrad", "längengrad", "laenge", "länge", "x" };
    private static readonly string[] LabelNames = { "name", "title", "titel", "bezeichnung", "ort", "standort", "label", "city", "stadt" };
    private static readonly string[] ArrayHints = { "coord", "loc", "pos", "geo", "punkt" };

    private readonly MongoClient _client;

    public GeoService(IConfiguration cfg)
    {
        _client = new MongoClient(cfg["Mongo:ConnectionString"] ?? "mongodb://localhost:27017");
    }

    public async Task<List<string>> GetDatabasesAsync()
    {
        var names = await (await _client.ListDatabaseNamesAsync()).ToListAsync();
        return names.Where(n => n is not ("admin" or "local" or "config")).OrderBy(n => n).ToList();
    }

    public async Task<List<string>> GetCollectionsAsync(string db)
    {
        var names = await (await _client.GetDatabase(db).ListCollectionNamesAsync()).ToListAsync();
        return names.Where(n => !n.StartsWith("system.")).OrderBy(n => n).ToList();
    }

    public async Task<LoadResult> LoadAsync(string db, string collection)
    {
        var col = _client.GetDatabase(db).GetCollection<BsonDocument>(collection);
        var docs = await col.Find(FilterDefinition<BsonDocument>.Empty).Limit(200_000).ToListAsync();

        var points = new List<GeoPoint>(docs.Count);
        foreach (var d in docs)
        {
            var p = ToPoint(d);
            if (p != null) points.Add(p);
        }
        return new LoadResult(points, docs.Count - points.Count);
    }

    private static GeoPoint? ToPoint(BsonDocument d)
    {
        if (!TryGetCoords(d, out var lat, out var lon)) return null;
        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;
        var id = d.GetValue("_id", "").ToString() ?? "";
        return new GeoPoint(id, GetLabel(d), lat, lon);
    }

    // Erkennt: lat/lon-Felder, GeoJSON {type:Point, coordinates:[lon,lat]} oder [lon,lat]-Arrays
    private static bool TryGetCoords(BsonDocument d, out double lat, out double lon)
    {
        lat = lon = 0;

        var la = FindNumber(d, LatNames);
        var lo = FindNumber(d, LonNames);
        if (la.HasValue && lo.HasValue) { lat = la.Value; lon = lo.Value; return true; }

        foreach (var e in d.Elements)
        {
            if (e.Value is BsonDocument sub)
            {
                if (sub.TryGetValue("coordinates", out var c) && c is BsonArray a && IsPair(a))
                {
                    lon = ToD(a[0])!.Value;
                    lat = ToD(a[1])!.Value;
                    return true;
                }
                if (TryGetCoords(sub, out lat, out lon)) return true;
            }
            else if (e.Value is BsonArray arr && IsPair(arr) &&
                     ArrayHints.Any(k => e.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                lon = ToD(arr[0])!.Value;
                lat = ToD(arr[1])!.Value;
                return true;
            }
        }
        return false;
    }

    private static bool IsPair(BsonArray a) => a.Count >= 2 && ToD(a[0]).HasValue && ToD(a[1]).HasValue;

    private static double? FindNumber(BsonDocument d, string[] names)
    {
        foreach (var n in names)
            foreach (var e in d.Elements)
                if (e.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                {
                    var v = ToD(e.Value);
                    if (v.HasValue) return v;
                }
        return null;
    }

    private static double? ToD(BsonValue v)
    {
        if (v.IsNumeric) return v.ToDouble();
        if (v.IsString && double.TryParse(v.AsString.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) return r;
        return null;
    }

    private static string GetLabel(BsonDocument d)
    {
        foreach (var n in LabelNames)
            foreach (var e in d.Elements)
                if (e.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && e.Value.IsString)
                    return e.Value.AsString;
        return d.GetValue("_id", "?").ToString() ?? "?";
    }
}
