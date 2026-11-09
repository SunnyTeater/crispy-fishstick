# Jellyfin Custom Branding

Plugin für Jellyfin 12, das Logo und Favicon des Webclients ersetzt.
Die Bilder werden im Dashboard hochgeladen und überschreiben die Originaldateien im Web-Ordner.

## Bauen

    ./build.sh

Braucht das .NET 10 SDK, aber nur auf dem Rechner, der baut. Ergebnis: `dist/CustomBranding_1.0.2.0/Jellyfin.Plugin.CustomBranding.dll`

Auf dem Jellyfin-Server selbst wird nichts zusätzlich installiert: Die Server-Pakete bringen ihre
.NET-Laufzeit mit, das Plugin ist eine einzelne DLL ohne weitere Abhängigkeiten.

## Installieren

Den Ordner `CustomBranding_1.0.2.0` in den Plugin-Ordner des Servers kopieren und Jellyfin neu starten:

    cp -r dist/CustomBranding_1.0.2.0 /var/lib/jellyfin/plugins/
    chown -R jellyfin: /var/lib/jellyfin/plugins/CustomBranding_1.0.2.0
    systemctl restart jellyfin

## Schreibrechte auf die Bilddateien

Das Plugin überschreibt Bilddateien im Web-Ordner, der Jellyfin-Dienst muss diese Dateien also
beschreiben dürfen. Beim Debian/Ubuntu-Paket gehören sie root, und jedes Jellyfin-Update legt sie
neu an. Deshalb die Dateien vor jedem Dienststart automatisch dem Benutzer `jellyfin` übereignen,
per systemd-Drop-in:

    mkdir -p /etc/systemd/system/jellyfin.service.d
    cat > /etc/systemd/system/jellyfin.service.d/custom-branding.conf <<'CONF'
    [Service]
    ExecStartPre=+/bin/sh -c "find /usr/share/jellyfin/web -maxdepth 2 -type f \\( -name 'banner-*.png' -o -name 'icon-transparent*.png' -o -name 'favicon*' -o -name 'touchicon*.png' \\) -exec chown jellyfin {} +"
    CONF
    systemctl daemon-reload
    systemctl restart jellyfin

Betroffen sind nur die Bilddateien. Der Ordner selbst und das JavaScript des Webclients bleiben
für den Dienst schreibgeschützt.

Ohne Drop-in geht es auch von Hand, dann aber nach jedem Jellyfin-Update erneut:

    find /usr/share/jellyfin/web -maxdepth 2 -type f \( -name 'banner-*.png' -o -name 'icon-transparent*.png' \
        -o -name 'favicon*' -o -name 'touchicon*.png' \) -exec chown jellyfin {} +
    systemctl restart jellyfin

Die Konfigurationsseite zeigt den Web-Ordner und eine Warnung an, falls die Rechte fehlen.

## Benutzen

Dashboard → Plugins → Custom Branding. Vier Bilder lassen sich ersetzen:

| Bild | Format | Ersetzt |
| --- | --- | --- |
| Logo für dunkle Themes | PNG, 1302 × 378 | `banner-light.*.png` |
| Logo für helle Themes | PNG, 1302 × 378 | `banner-dark.*.png` |
| Icon | PNG, 536 × 536 | `icon-transparent.*.png` (in Jellyfin 12 das Logo oben links und im Seitenmenü) |
| Favicon | PNG (256 × 256) oder ICO | `favicon.*.ico`, bei PNG zusätzlich alle `touchicon*.png` |

Nach dem Hochladen den Browser mit Strg+F5 neu laden, die alten Bilder liegen im Cache.

## Verhalten

- Die Originale werden vor dem Überschreiben nach `<data>/custom-branding/backup` gesichert,
  „Original wiederherstellen“ spielt sie zurück.
- Ein Jellyfin-Update ersetzt den Web-Ordner. Beim nächsten Serverstart spielt das Plugin die
  eigenen Bilder automatisch wieder ein, sofern es die Dateien beschreiben darf (dafür das Drop-in oben).
- Symlinks im Web-Ordner werden nicht überschrieben.
- Die Bilder werden nicht skaliert. Ein PNG-Favicon wird unverändert in alle Touch-Icon-Dateien
  geschrieben und für `favicon.ico` in einen ICO-Container verpackt.
- Die API (`/CustomBranding/...`) ist nur für Administratoren erreichbar.
