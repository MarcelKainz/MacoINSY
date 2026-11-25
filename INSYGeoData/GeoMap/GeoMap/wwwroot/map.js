window.geoMap = (() => {
  let map, layer, dotnet, pin, circle, handle;
  let mode = "pin";
  let inside = new Set();
  let selectedId = null;
  let lastNotify = 0;
  const markers = new Map(); // id -> L.Marker

  L.Icon.Default.imagePath = "lib/leaflet/images/";

  const mkIcon = (cls, size) => L.divIcon({ className: cls, iconSize: [size, size], iconAnchor: [size / 2, size / 2] });
  const icons = {
    n: mkIcon("pt", 16),
    i: mkIcon("pt in", 16),
    s: mkIcon("pt sel", 16),
    si: mkIcon("pt in sel", 16),
    handle: mkIcon("rh", 14),
  };

  const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
  const call = (name, ...args) => { try { dotnet && dotnet.invokeMethodAsync(name, ...args); } catch { } };

  async function init(id, dotnetRef) {
    dotnet = dotnetRef;
    if (map) { map.remove(); map = null; markers.clear(); pin = circle = handle = null; }
    map = L.map(id, { worldCopyJump: true, minZoom: 1 }).setView([47.5, 14.5], 7);

    // Offline-Basiskarte: Ländergrenzen aus lokaler GeoJSON-Datei
    try {
      const res = await fetch("data/world.geo.json");
      const geo = await res.json();
      L.geoJSON(geo, { interactive: false, style: { color: "#6b7280", weight: 1, fillColor: "#f3f4f6", fillOpacity: 1 } }).addTo(map);
    } catch (e) { console.warn("world.geo.json nicht geladen", e); }

    // Optional: lokale Kacheln (wwwroot/tiles/{z}/{x}/{y}.png), falls vorhanden
    try {
      const t = await fetch("tiles/0/0/0.png", { method: "HEAD" });
      if (t.ok && (t.headers.get("content-type") || "").startsWith("image"))
        L.tileLayer("tiles/{z}/{x}/{y}.png", { maxZoom: 19 }).addTo(map);
    } catch { }

    layer = L.layerGroup().addTo(map);
    L.control.scale({ imperial: false }).addTo(map);
    map.on("click", e => call("OnMapClick", e.latlng.lat, e.latlng.lng));
  }

  // ---------- Punkte ----------

  function stateIcon(id) {
    const i = inside.has(id), s = id === selectedId;
    return s ? (i ? icons.si : icons.s) : (i ? icons.i : icons.n);
  }

  function refresh(id) {
    const m = markers.get(id);
    if (!m) return;
    const ic = stateIcon(id);
    if (m._ic !== ic) { m.setIcon(ic); m._ic = ic; }
    m.setZIndexOffset(id === selectedId ? 1000 : 0);
  }

  function addMarker(p) {
    const m = L.marker([p.lat, p.lon], { icon: icons.n, draggable: mode === "edit", bubblingMouseEvents: false });
    m._ic = icons.n;
    m.bindTooltip(esc(p.label));
    m.on("click", () => call("OnPointSelected", p.id));
    m.on("dragend", () => { const ll = m.getLatLng(); call("OnPointDragged", p.id, ll.lat, ll.lng); });
    m.addTo(layer);
    markers.set(p.id, m);
  }

  function setPoints(points, fit) {
    layer.clearLayers();
    markers.clear();
    inside = new Set();
    selectedId = null;
    const bounds = [];
    for (const p of points) { addMarker(p); bounds.push([p.lat, p.lon]); }
    if (!fit) return;
    if (bounds.length === 1) map.setView(bounds[0], 13);
    else if (bounds.length > 1) map.fitBounds(bounds, { padding: [30, 30] });
  }

  function setInside(ids) {
    const old = inside;
    inside = new Set(ids);
    for (const id of old) if (!inside.has(id)) refresh(id);
    for (const id of inside) if (!old.has(id)) refresh(id);
  }

  function selectPoint(id, pan) {
    const prev = selectedId;
    selectedId = id;
    if (prev) refresh(prev);
    if (id) {
      refresh(id);
      const m = markers.get(id);
      if (pan && m) map.panTo(m.getLatLng());
    }
  }

  function updatePoint(p) {
    const m = markers.get(p.id);
    if (!m) return;
    m.setLatLng([p.lat, p.lon]);
    m.setTooltipContent(esc(p.label));
  }

  function addPoint(p) { addMarker(p); }

  function removePoint(id) {
    const m = markers.get(id);
    if (m) layer.removeLayer(m);
    markers.delete(id);
    inside.delete(id);
    if (selectedId === id) selectedId = null;
  }

  function setMode(m) {
    mode = m;
    markers.forEach(mk => (m === "edit" ? mk.dragging.enable() : mk.dragging.disable()));
  }

  function setCursor(crosshair) {
    map.getContainer().classList.toggle("crosshair", !!crosshair);
  }

  // ---------- Pin + Radius ----------

  const east = (lat, lon, r) => {
    const cos = Math.max(0.01, Math.cos(lat * Math.PI / 180));
    return [lat, lon + r / (111320 * cos)];
  };

  function notifyPin(force) {
    const now = Date.now();
    if (!force && now - lastNotify < 90) return;
    lastNotify = now;
    const ll = pin.getLatLng();
    call("OnPinChanged", ll.lat, ll.lng, circle.getRadius());
  }

  function placeHandle() {
    const c = pin.getLatLng();
    handle.setLatLng(east(c.lat, c.lng, circle.getRadius()));
  }

  function setPin(lat, lon, radiusM, fit) {
    if (!pin) {
      circle = L.circle([lat, lon], { radius: radiusM, color: "#2563eb", weight: 2, fillColor: "#3b82f6", fillOpacity: 0.12, interactive: false }).addTo(map);
      pin = L.marker([lat, lon], { draggable: true, zIndexOffset: 2000, bubblingMouseEvents: false }).addTo(map);
      pin.bindTooltip("Pin: ziehen zum Verschieben");
      handle = L.marker(east(lat, lon, radiusM), { icon: icons.handle, draggable: true, zIndexOffset: 2500, bubblingMouseEvents: false }).addTo(map);
      handle.bindTooltip("Ziehen = Radius ändern");

      pin.on("drag", () => { circle.setLatLng(pin.getLatLng()); placeHandle(); notifyPin(false); });
      pin.on("dragend", () => notifyPin(true));

      handle.on("drag", () => {
        const r = Math.max(10, pin.getLatLng().distanceTo(handle.getLatLng()));
        circle.setRadius(r);
        notifyPin(false);
      });
      handle.on("dragend", () => { placeHandle(); notifyPin(true); });
    } else {
      pin.setLatLng([lat, lon]);
      circle.setLatLng([lat, lon]);
      circle.setRadius(radiusM);
      placeHandle();
    }
    if (fit) map.fitBounds(circle.getBounds(), { padding: [30, 30] });
  }

  function clearPin() {
    if (!pin) return;
    map.removeLayer(pin); map.removeLayer(circle); map.removeLayer(handle);
    pin = circle = handle = null;
  }

  return { init, setPoints, setInside, selectPoint, updatePoint, addPoint, removePoint, setMode, setCursor, setPin, clearPin };
})();
