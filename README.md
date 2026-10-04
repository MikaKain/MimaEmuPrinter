# MimaEmuPrinter

Émulateur d'imprimante thermique réseau IP 58 / 80 mm (protocole ESC/POS sur TCP brut, port 9100 par défaut), destiné au test d'un logiciel de caisse de restaurant. Il accepte le flux ESC/POS, répond aux interrogations de statut, affiche le ticket virtuel, journalise chaque arrivée et archive les impressions en PDF. Son intérêt principal : provoquer à la demande les pannes que la caisse doit gérer (hors ligne, capot ouvert, fin de papier, near-end, erreur massicot, erreurs récupérable / irrécupérable).

Spécification : `MimaEmuPrinter-cahier-des-charges.pdf` (v1.0). C# / .NET 10, interface Avalonia UI 11 (Windows, macOS, Linux), aucune API Win32.

## Lancer

```bash
dotnet run --project src/MimaEmuPrinter.App
```

Ou publier un binaire (runtime .NET 10 requis sur la machine cible) :

```bash
dotnet publish src/MimaEmuPrinter.App -c Release -r win-x64 --self-contained false
dotnet publish src/MimaEmuPrinter.App -c Release -r linux-x64 --self-contained false
dotnet publish src/MimaEmuPrinter.App -c Release -r osx-arm64 --self-contained false
```

Le dossier de travail de l'application est le dossier de l'exécutable. On y trouve :

| Élément | Rôle |
| --- | --- |
| `settings.json` | IP d'écoute, port et laize, restaurés au lancement |
| `pdf/tickets-AAAA-MM-JJ.pdf` | PDF du jour : A4, 2 colonnes de 5 tickets, réécrit à chaque job clos |
| `pdf/journal.log` | Journal complet (l'écran garde les 500 dernières lignes) |
| `pdf/brut/` | Par job : flux ESC/POS brut (`.bin`) et données du ticket (`.json`, qui servent à reconstruire le PDF du jour après un redémarrage) |

Options de ligne de commande, pour l'automatisation : `--start` (écoute dès le lancement), `--output <dossier>` (remplace `pdf`), `--settings <fichier>`.

## Utilisation

1. **Au démarrage** (service arrêté), la fenêtre ne montre que les réglages : adresse IP d'écoute (par défaut la première IPv4 non loopback ; `127.0.0.1` et `0.0.0.0` sont proposées), port et laize (58 mm / 32 colonnes, 80 mm / 42 colonnes, 80 mm / 48 colonnes), puis **Démarrer**. Ces trois réglages ne se changent plus tant que le service tourne ; pour les modifier, arrêter puis redémarrer. Un échec de bind (port pris) apparaît dans le journal.
2. **Service démarré**, la fenêtre se réduit à l'affichage temps réel et au journal des arrivées. L'indicateur affiche *En écoute* ou *Occupée* (connexion de caisse ouverte), avec l'IP, le port et la laize en sous-titre.
   - Le ticket s'affiche au fil de la réception, reste 5 secondes après la fin du job (compte à rebours), puis revient à *En attente*. *Garder* le fige jusqu'au job suivant ou jusqu'à *Relâcher*.
   - **Événements** ouvre la page des neuf pannes dans une fenêtre séparée, à garder à côté. Effet immédiat, y compris pendant une connexion. Les conséquences sont imposées : *Plus de papier* allume *Arrêt fin de papier*, *Erreur* et *Offline* ; *Capot ouvert* allume *Offline* ; *Erreur massicot / récupérable / irrécupérable* allument *Erreur* et *Offline*. Un interrupteur impliqué est affiché allumé et verrouillé tant que sa cause est active. *Tout effacer* revient à l'état en ligne.
   - **Arrêter le service** ferme l'écoute et la connexion ouverte, et ramène l'écran de réglages.

## Architecture

```
src/MimaEmuPrinter.Core   bibliothèque sans dépendance UI : serveur TCP, parseur ESC/POS, moteur de statut,
                          rendu du ticket, journal, archivage PDF (PDFsharp, polices Liberation Mono / Sans embarquées)
src/MimaEmuPrinter.App    interface Avalonia : affiche l'état et envoie des commandes au cœur
tests/MimaEmuPrinter.Tests  xUnit : statuts, parseur, rendu, scénarios T1 à T9 sur de vrais sockets
tools/MimaEmuPrinter.TestClient  « caisse » en ligne de commande
```

## Tests

```bash
dotnet test
```

Les scénarios T1 à T9 du cahier des charges et les critères d'acceptation automatisables sont couverts par `AcceptanceScenarioTests` et `ConnectionBehaviorTests` (sockets réels sur 127.0.0.1, port éphémère).

Pour un test manuel, avec l'émulateur en écoute :

```bash
dotnet run --project tools/MimaEmuPrinter.TestClient -- ticket --logo       # ticket de table de référence
dotnet run --project tools/MimaEmuPrinter.TestClient -- status 4            # DLE EOT 4, affiche l'octet
dotnet run --project tools/MimaEmuPrinter.TestClient -- status 1 --expect 0x1A   # code retour 1 si l'octet diffère
dotnet run --project tools/MimaEmuPrinter.TestClient -- asb 30              # arme l'ASB et affiche les 4 octets reçus
dotnet run --project tools/MimaEmuPrinter.TestClient -- --help
```

## Choix d'interprétation du cahier des charges

- **Adresse d'écoute** : le § 3 cite `0.0.0.0` par défaut, le § 4.1 propose la première IPv4 non loopback. L'adresse choisie est celle du bind ; la première IPv4 non loopback est préselectionnée et `0.0.0.0` reste sélectionnable.
- **Texte trop large** : tronqué avec le marqueur `»` (rouge) et noté dans le journal (§ 4.2). Seules les lignes contenant une URL (`://`) et les lignes `[QR]` / `[BAR]` passent à la ligne, comme dans l'aperçu de référence (§ 6).
- **Impression bloquée** : *Offline*, *Arrêt fin de papier* et *Plus de papier* (donc aussi capot ouvert et les trois erreurs, qui allument *Offline*) bloquent l'impression. Avant le premier octet imprimable : job vide, cartouche « non imprimé : … ». En cours de job : lignes déjà reçues conservées, cartouche « tronqué : … », la suite du job est ignorée. *Papier bientôt fini* et *Erreur* seule n'empêchent pas d'imprimer.
- **« Premier octet imprimable »** : texte, saut de ligne, avance papier, code-barres, QR ou bitmap ; `ESC @` et les commandes de statut ne démarrent pas de job.
- **ASB** : `GS a n` avec `n` non nul arme l'ASB (valeur mémorisée pour la connexion), `n = 0` le désarme. Les 4 octets sont émis à chaque changement effectif d'état, pas à l'armement.
- **Octets de statut** : bits fixes Epson respectés (`0x12` en ligne). ASB : octet 1 = `0x10` + offline `0x08` + capot `0x20` ; octet 2 = massicot `0x08`, irrécupérable `0x20`, récupérable `0x40` ; octet 3 = near-end `0x03`, fin de papier `0x0C` ; octet 4 = 0. `GS I` : 67 = modèle `MimaEmuPrinter`, 65 = version `1.0`, 66 = fabricant, 1 / 2 / 3 = octets d'identité.
- **Numéro de job** : `J0001`, `J0002`… par jour calendaire ; la numérotation et le PDF du jour reprennent après un redémarrage.
- **PDF verrouillé** : si un lecteur PDF verrouille le fichier (Windows), le remplacement est reporté au job suivant et noté dans le journal.
