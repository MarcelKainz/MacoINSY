using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GeoMap.Services;

public record GeoPoint(string Id, string IdType, string Label, double Lat, double Lon, string Details);

public record LoadResult(List<GeoPoint> Points, int Total, int Skipped);

public enum CoordKind { LatLon, Array }

// LatLon: A = Pfad zum Lat-Feld, B = Pfad zum Lon-Feld. Array: A = Pfad zum [lon,lat]-Array (z.B. location.coordinates)
public record CoordSpec(CoordKind Kind, string[] A, string[] B);

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

    private IMongoCollection<BsonDocument> Col(string db, string collection) =>
        _client.GetDatabase(db).GetCollection<BsonDocument>(collection);

    // ---------- Lesen ----------

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

    public async Task<LoadResult> LoadAsync(string db, string collection, int limit, bool swap)
    {
        var docs = await Col(db, collection).Find(FilterDefinition<BsonDocument>.Empty).Limit(limit).ToListAsync();
        var points = new List<GeoPoint>();
        foreach (var d in docs)
        {
            var p = ToPoint(d, swap);
            if (p != null) points.Add(p);
        }
        return new LoadResult(points, docs.Count, docs.Count - points.Count);
    }

    // ---------- Schreiben ----------

    public async Task UpdateAsync(string db, string collection, string id, string idType,
                                  string label, double lat, double lon, bool swap)
    {
        var col = Col(db, collection);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", ParseId(id, idType));
        var doc = await col.Find(filter).FirstOrDefaultAsync()
                  ?? throw new InvalidOperationException("Dokument nicht gefunden");
        var spec = FindSpec(doc, Array.Empty<string>())
                   ?? throw new InvalidOperationException("Keine Koordinatenfelder im Dokument gefunden");

        if (swap) (lat, lon) = (lon, lat);
        ApplyCoords(doc, spec, lat, lon);
        SetLabel(doc, label);

        var res = await col.ReplaceOneAsync(filter, doc);
        if (res.MatchedCount == 0) throw new InvalidOperationException("Dokument nicht gefunden");
    }

    public async Task DeleteAsync(string db, string collection, string id, string idType)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", ParseId(id, idType));
        await Col(db, collection).DeleteOneAsync(filter);
    }

    public async Task<GeoPoint> InsertAsync(string db, string collection, string label, double lat, double lon, bool swap)
    {
        var col = Col(db, collection);

        // Aufbau eines vorhandenen Dokuments als Vorlage übernehmen (Feldnamen für Koordinaten und Label)
        CoordSpec? spec = null;
        string labelField = "name";
        var samples = await col.Find(FilterDefinition<BsonDocument>.Empty).Limit(50).ToListAsync();
        foreach (var s in samples)
        {
            spec = FindSpec(s, Array.Empty<string>());
            if (spec != null)
            {
                labelField = FindLabelField(s) ?? "name";
                break;
            }
        }
        spec ??= new CoordSpec(CoordKind.LatLon, new[] { "lat" }, new[] { "lon" });

        if (swap) (lat, lon) = (lon, lat);
        var doc = new BsonDocument(labelField, label);
        ApplyCoords(doc, spec, lat, lon);

        await col.InsertOneAsync(doc);
        return ToPoint(doc, swap) ?? throw new InvalidOperationException("Standort konnte nicht angelegt werden");
    }

    // ---------- Koordinaten finden / lesen / schreiben ----------

    private static GeoPoint? ToPoint(BsonDocument d, bool swap)
    {
        var spec = FindSpec(d, Array.Empty<string>());
        if (spec == null || !ReadCoords(d, spec, out var lat, out var lon)) return null;
        if (swap) (lat, lon) = (lon, lat);
        if (lat is < -90 or > 90 || lon is < -180 or > 180) return null;

        var idv = d.GetValue("_id", BsonNull.Value);
        return new GeoPoint(idv.ToString() ?? "", idv.BsonType.ToString(), GetLabel(d), lat, lon, GetDetails(d));
    }

    private static CoordSpec? FindSpec(BsonDocument d, string[] prefix)
    {
        var la = FindName(d, LatNames);
        var lo = FindName(d, LonNames);
        if (la != null && lo != null)
            return new CoordSpec(CoordKind.LatLon, prefix.Append(la).ToArray(), prefix.Append(lo).ToArray());

        foreach (var e in d.Elements)
        {
            if (e.Value is BsonDocument sub)
            {
                if (sub.TryGetValue("coordinates", out var c) && c is BsonArray a && IsPair(a))
                    return new CoordSpec(CoordKind.Array, prefix.Append(e.Name).Append("coordinates").ToArray(), Array.Empty<string>());

                var inner = FindSpec(sub, prefix.Append(e.Name).ToArray());
                if (inner != null) return inner;
            }
            else if (e.Value is BsonArray arr && IsPair(arr) &&
                     ArrayHints.Any(k => e.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return new CoordSpec(CoordKind.Array, prefix.Append(e.Name).ToArray(), Array.Empty<string>());
            }
        }
        return null;
    }

    private static bool ReadCoords(BsonDocument d, CoordSpec s, out double lat, out double lon)
    {
        lat = lon = 0;
        if (s.Kind == CoordKind.LatLon)
        {
            var la = GetPath(d, s.A) is { } a ? ToD(a) : null;
            var lo = GetPath(d, s.B) is { } b ? ToD(b) : null;
            if (!la.HasValue || !lo.HasValue) return false;
            lat = la.Value; lon = lo.Value;
            return true;
        }
        if (GetPath(d, s.A) is BsonArray arr && IsPair(arr))
        {
            lon = ToD(arr[0])!.Value;
            lat = ToD(arr[1])!.Value;
            return true;
        }
        return false;
    }

    private static void ApplyCoords(BsonDocument d, CoordSpec s, double lat, double lon)
    {
        if (s.Kind == CoordKind.LatLon)
        {
            SetNumber(d, s.A, lat);
            SetNumber(d, s.B, lon);
            return;
        }

        var parent = EnsureParent(d, s.A);
        var key = s.A[^1];
        if (parent.TryGetValue(key, out var v) && v is BsonArray arr && arr.Count >= 2)
        {
            arr[0] = new BsonDouble(lon);
            arr[1] = new BsonDouble(lat);
        }
        else
        {
            if (key == "coordinates") parent["type"] = "Point";
            parent[key] = new BsonArray { new BsonDouble(lon), new BsonDouble(lat) };
        }
    }

    private static void SetNumber(BsonDocument d, string[] path, double value)
    {
        var parent = EnsureParent(d, path);
        var key = path[^1];
        var wasString = parent.TryGetValue(key, out var old) && old.IsString;
        parent[key] = wasString
            ? new BsonString(value.ToString("R", CultureInfo.InvariantCulture))
            : new BsonDouble(value);
    }

    private static BsonValue? GetPath(BsonDocument d, string[] path)
    {
        BsonValue cur = d;
        foreach (var k in path)
        {
            if (cur is BsonDocument bd && bd.TryGetValue(k, out var next)) cur = next;
            else return null;
        }
        return cur;
    }

    private static BsonDocument EnsureParent(BsonDocument d, string[] path)
    {
        var cur = d;
        for (var i = 0; i < path.Length - 1; i++)
        {
            BsonDocument nd;
            if (cur.TryGetValue(path[i], out var next) && next is BsonDocument existing) nd = existing;
            else
            {
                nd = new BsonDocument();
                cur[path[i]] = nd;
            }
            cur = nd;
        }
        return cur;
    }

    private static bool IsPair(BsonArray a) => a.Count >= 2 && ToD(a[0]).HasValue && ToD(a[1]).HasValue;

    private static string? FindName(BsonDocument d, string[] names)
    {
        foreach (var n in names)
            foreach (var e in d.Elements)
                if (e.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && ToD(e.Value).HasValue)
                    return e.Name;
        return null;
    }

    private static double? ToD(BsonValue v)
    {
        if (v.IsNumeric) return v.ToDouble();
        if (v.IsString && double.TryParse(v.AsString.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) return r;
        return null;
    }

    // ---------- Label / Details / Id ----------

    private static string? FindLabelField(BsonDocument d)
    {
        foreach (var n in LabelNames)
            foreach (var e in d.Elements)
                if (e.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && e.Value.IsString)
                    return e.Name;
        return null;
    }

    private static string GetLabel(BsonDocument d)
    {
        var f = FindLabelField(d);
        if (f != null) return d[f].AsString;
        return d.GetValue("_id", "?").ToString() ?? "?";
    }

    private static void SetLabel(BsonDocument d, string label)
    {
        var f = FindLabelField(d);
        if (f != null) d[f] = label;
        else if (label != GetLabel(d)) d["name"] = label;
    }

    private static string GetDetails(BsonDocument d)
    {
        var parts = d.Elements
            .Where(e => e.Name != "_id" && (e.Value.IsString || e.Value.IsNumeric || e.Value.IsBoolean || e.Value.IsValidDateTime))
            .Take(12)
            .Select(e => $"{e.Name}: {e.Value}");
        return string.Join("\n", parts);
    }

    private static BsonValue ParseId(string id, string type) => type switch
    {
        "ObjectId" => new BsonObjectId(ObjectId.Parse(id)),
        "Int32" => new BsonInt32(int.Parse(id, CultureInfo.InvariantCulture)),
        "Int64" => new BsonInt64(long.Parse(id, CultureInfo.InvariantCulture)),
        _ => new BsonString(id)
    };
}
