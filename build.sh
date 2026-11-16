#!/bin/sh
# Baut das Plugin und legt den fertigen Plugin-Ordner unter ./dist ab.
# Braucht das .NET 10 SDK - nur auf dem Rechner, der baut, nicht auf dem Jellyfin-Server.
set -e
cd "$(dirname "$0")"
OUT="dist/CustomBranding_1.0.2.0"

dotnet publish -c Release -o "$OUT"

# Nur die Plugin-DLL wird gebraucht, alles andere bringt der Server mit.
find "$OUT" -type f ! -name 'Jellyfin.Plugin.CustomBranding.dll' -delete
echo "Fertig: $OUT"
