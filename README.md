# Jellyfin Custom Branding

Plugin für Jellyfin 12, das Logo und Favicon des Webclients ersetzt.
Die Bilder werden im Dashboard hochgeladen und überschreiben die Originaldateien im Web-Ordner.

## Bauen

    ./build.sh

Braucht das .NET 10 SDK, aber nur auf dem Rechner, der baut. Ergebnis: `dist/CustomBranding_1.0.0.0/Jellyfin.Plugin.CustomBranding.dll`

Auf dem Jellyfin-Server selbst wird nichts zusätzlich installiert: Die Server-Pakete bringen ihre
.NET-Laufzeit mit, das Plugin ist eine einzelne DLL ohne weitere Abhängigkeiten.

## Installieren

Den Ordner `CustomBranding_1.0.0.0` in den Plugin-Ordner des Servers kopieren und Jellyfin neu starten:

    cp -r dist/CustomBranding_1.0.0.0 /var/lib/jellyfin/plugins/
    chown -R jellyfin: /var/lib/jellyfin/plugins/CustomBranding_1.0.0.0
    systemctl restart jellyfin

## Schreibrechte auf den Web-Ordner

Das Plugin überschreibt Dateien im Web-Ordner, der Jellyfin-Dienst muss dort also schreiben dürfen.
Beim Debian/Ubuntu-Paket gehört der Ordner root, deshalb einmalig:

    setfacl -R -m u:jellyfin:rwX /usr/share/jellyfin/web

Die Konfigurationsseite zeigt den Web-Ordner und eine Warnung an, falls die Rechte fehlen.

## Benutzen

Dashboard → Plugins → Custom Branding. Vier Bilder lassen sich ersetzen:

| Bild | Format | Ersetzt |
| --- | --- | --- |
| Logo für dunkle Themes | PNG, 1302 × 378 | `banner-light.*.png` |
| Logo für helle Themes | PNG, 1302 × 378 | `banner-dark.*.png` |
| Icon | PNG, 536 × 536 | `icon-transparent.*.png` |
| Favicon | PNG (256 × 256) oder ICO | `favicon.*.ico`, bei PNG zusätzlich alle `touchicon*.png` |

Nach dem Hochladen den Browser mit Strg+F5 neu laden, die alten Bilder liegen im Cache.

## Verhalten

- Die Originale werden vor dem Überschreiben nach `<data>/custom-branding/backup` gesichert,
  „Original wiederherstellen“ spielt sie zurück.
- Ein Jellyfin-Update ersetzt den Web-Ordner. Beim nächsten Serverstart spielt das Plugin die
  eigenen Bilder automatisch wieder ein.
- Die Bilder werden nicht skaliert. Ein PNG-Favicon wird unverändert in alle Touch-Icon-Dateien
  geschrieben und für `favicon.ico` in einen ICO-Container verpackt.
- Die API (`/CustomBranding/...`) ist nur für Administratoren erreichbar.
