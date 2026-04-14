# 🚀 DropCast — Roadmap, Améliorations & Stratégie de Monétisation

---

## 📋 Table des matières

1. [Audit technique — État actuel](#audit-technique)
2. [Améliorations techniques prioritaires](#améliorations-techniques)
3. [Nouvelles fonctionnalités](#nouvelles-fonctionnalités)
4. [Stratégie de monétisation](#stratégie-de-monétisation)
5. [Stratégie communautaire](#stratégie-communautaire)
6. [Roadmap par phases](#roadmap-par-phases)

---

## 🔍 Audit technique — État actuel {#audit-technique}

### Architecture

| Composant | Technologie | Remarque |
|---|---|---|
| Windows Desktop | WinForms / .NET Framework 4.7.2 | Ancien, non cross-platform |
| Android | .NET MAUI / net10.0-android | Moderne |
| Vidéo (Windows) | LibVLCSharp | Bonne base |
| Résolution YouTube | YoutubeExplode (pure .NET) | ✅ Pas de binaire externe |
| Résolution TikTok/Insta | yt-dlp (auto-download AppData) | ✅ Conforme aux règles du projet |
| Discord | Discord.Net.WebSocket 3.17.1 | À jour |
| Stockage token | AES-256-CBC chiffré | ✅ |
| Settings | JSON dans %AppData%/DropCast | ✅ |
| Pipeline | `IMessageSource` → `MessagePipeline` → `IMediaDisplay` | Bonne abstraction |

### ✅ Points forts

- Architecture propre avec abstractions (`IMessageSource`, `IMediaDisplay`, `MessagePipeline`)
- Multi-plateforme (Windows + Android) avec des codebases séparées mais des patterns similaires
- Résolution multi-plateforme (YouTube, TikTok, Instagram, Twitter, Reddit)
- Chiffrement du token Discord
- Système de trimming vidéo avec syntaxe intuitive `[start-end]`
- Drag & drop natif Windows avec overlay dédié
- Cache des résolutions URL (TTL 1h)

### ⚠️ Points d'amélioration identifiés

- **Duplication de code** entre Windows et Android (UrlDetector, TrimParser, VideoResolver, TokenProvider, modèles) — aucune bibliothèque partagée
- **WinForms + .NET Framework 4.7.2** — technologie vieillissante, pas de support long-terme
- **Pas de tests unitaires** — aucun projet de tests dans la solution
- **Pas de CI/CD** — pas de GitHub Actions pour build/release automatique
- **Pas de système de mise à jour automatique** (Windows)
- **Pas de gestion d'erreurs centralisée** — catch vides dans `UserSettings.Save()`
- **Pas de file d'attente de memes** — un seul meme à la fois, les autres sont refusés
- **Pas de modération/filtrage** — tout contenu est affiché tel quel
- **Interface en français uniquement** — limite l'adoption internationale

---

## 🔧 Améliorations techniques prioritaires {#améliorations-techniques}

### 🏗️ P0 — Fondations

| Amélioration | Description | Impact |
|---|---|---|
| **Bibliothèque partagée** | Créer un projet `DropCast.Core` (netstandard2.0 ou net8.0) avec les classes communes : `TrimParser`, `UrlDetector`, `TokenProvider`, modèles, abstractions | Réduit la duplication, facilite la maintenance |
| **Tests unitaires** | Ajouter un projet xUnit pour tester `TrimParser`, `UrlDetector`, `VideoResolver`, `MessagePipeline` | Fiabilité, confiance pour refactorer |
| **CI/CD GitHub Actions** | Build + test automatique sur push, release automatique avec artifacts (exe + apk) | Professionnalisme, rapidité de release |
| **Logging structuré** | Remplacer les `catch { }` vides par un vrai logging d'erreur partout | Débugage en production |

### 🔄 P1 — Modernisation

| Amélioration | Description | Impact |
|---|---|---|
| **Migration vers .NET 8+ (Windows)** | Migrer le projet WinForms de .NET Framework 4.7.2 vers .NET 8/9 | Performances, support long-terme, single-file publish |
| **Auto-update (Windows)** | Implémenter un check de version GitHub Releases au démarrage + téléchargement en arrière-plan | Rétention utilisateurs |
| **File d'attente de memes** | Remplacer le refus direct par une queue FIFO avec affichage séquentiel | Plus de memes affichés = plus d'engagement |
| **Retry & résilience** | Ajouter Polly pour les appels HTTP (résolution vidéo, téléchargement) avec retry + circuit breaker | Stabilité |

### ⚡ P2 — Performance & UX

| Amélioration | Description | Impact |
|---|---|---|
| **Pré-téléchargement** | Pendant qu'un meme est affiché, pré-télécharger/résoudre le suivant dans la queue | Transitions plus fluides |
| **Animations de transition** | Fade-in/fade-out entre les memes au lieu de l'apparition/disparition brute | UX premium |
| **Thème sombre/clair** | Permettre la personnalisation du thème de l'overlay et des fenêtres | Personnalisation |
| **Cache d'images disque** | Cacher les images téléchargées localement pour les re-affichages | Réduction de bande passante |

---

## ✨ Nouvelles fonctionnalités {#nouvelles-fonctionnalités}

### 🎯 Haute priorité — Différenciateurs

| Fonctionnalité | Description | Plateforme |
|---|---|---|
| **🗳️ Système de vote** | Les viewers votent via réaction Discord pour le prochain meme à afficher | Win + Android |
| **🎵 Support Spotify/SoundCloud** | Afficher la pochette + un extrait audio quand quelqu'un envoie un lien musical | Win + Android |
| **📊 Dashboard de stats** | Nombre de memes affichés, top contributeurs, memes les plus populaires | Win + Android |
| **🔒 Modération** | Blacklist de mots/URLs, NSFW filter (via API de modération d'image), cooldown par utilisateur | Win + Android |
| **⏱️ Queue visible** | Afficher un compteur "3 memes en attente" sur l'overlay | Win + Android |
| **🎨 OBS WebSocket** | Source navigateur OBS avec WebSocket — plus besoin de window capture | Win (web) |

### 🌟 Moyenne priorité — Engagement

| Fonctionnalité | Description | Plateforme |
|---|---|---|
| **💬 Support Twitch Chat** | Nouveau `IMessageSource` pour Twitch IRC — les viewers Twitch peuvent aussi envoyer des memes | Win + Android |
| **🎮 Intégration StreamElements/Streamlabs** | Les donations/events déclenchent des overlays spéciaux | Win |
| **🏆 Système de points** | Les viewers accumulent des points pour envoyer des memes (cooldown naturel) | Win + Android |
| **📱 Télécommande web** | Interface web locale pour contrôler l'overlay (volume, skip, pause) sans alt-tab | Win |
| **🖼️ Templates d'overlay** | Cadres/bordures personnalisables autour des memes (style "cadre doré", "néon", etc.) | Win + Android |
| **🔊 Soundboard** | Commandes Discord (ex: `!airhorn`) qui jouent des sons prédéfinis | Win + Android |

### 💡 Nice-to-have

| Fonctionnalité | Description | Plateforme |
|---|---|---|
| **🌐 Version Web (Electron/Blazor)** | Client web standalone pour macOS/Linux | Cross-platform |
| **📺 Multi-écran** | Choisir sur quel moniteur l'overlay s'affiche | Win |
| **🎬 Mode replay** | Rejouer les X derniers memes affichés | Win + Android |
| **📤 Export clip** | Capturer les 10 dernières secondes de l'overlay en GIF/MP4 | Win |
| **🤖 Bot Discord slash commands** | `/meme`, `/queue`, `/skip`, `/stats` directement dans Discord | Discord |

---

## 💰 Stratégie de monétisation {#stratégie-de-monétisation}

### Modèle Freemium recommandé

> **Principe** : L'app gratuite est déjà meilleure que tout ce qui existe. La version Pro ajoute du confort et des features de power-user.

### 🆓 DropCast Free

| Feature | Inclus |
|---|---|
| Overlay transparent images/vidéos/texte | ✅ |
| Support YouTube, TikTok, Instagram | ✅ |
| 1 source (Discord) | ✅ |
| File d'attente (max 3 memes) | ✅ |
| Trimming vidéo | ✅ |
| Drag & drop local | ✅ |
| Historique canaux (5 derniers) | ✅ |
| Branding "Powered by DropCast" discret | ✅ |

### ⭐ DropCast Pro — 4,99€/mois ou 39,99€/an

| Feature | Pro uniquement |
|---|---|
| Queue illimitée | ✅ |
| Multi-source (Discord + Twitch Chat) | ✅ |
| Modération avancée (NSFW filter, blacklist) | ✅ |
| Dashboard de stats | ✅ |
| OBS WebSocket source | ✅ |
| Animations de transition personnalisées | ✅ |
| Templates d'overlay premium | ✅ |
| Soundboard | ✅ |
| Pas de branding | ✅ |
| Support prioritaire | ✅ |
| Historique canaux illimité | ✅ |

### 💎 Revenus additionnels

| Source de revenu | Description | Potentiel |
|---|---|---|
| **Templates marketplace** | Vendre des packs de templates d'overlay (5-10 templates pour 2,99€) | Récurrent |
| **Soundboard packs** | Packs de sons thématiques (memes classiques, gaming, etc.) | Récurrent |
| **Donations/Tips** | Page Ko-fi / Buy Me a Coffee intégrée dans l'app | Passif |
| **Sponsoring** | Partenariats avec des marques gaming/streaming (bannière dans le dashboard) | Moyen-terme |
| **Affiliation** | Liens d'affiliation vers des équipements streaming recommandés | Passif |

### 🏦 Infrastructure de paiement

| Option | Avantages | Inconvénients |
|---|---|---|
| **Gumroad** | Simple, gère les taxes EU, pas de backend | 10% de commission |
| **LemonSqueezy** | Merchant of Record (gère TVA EU), API propre | 5%+50¢ par transaction |
| **Stripe + propre backend** | Contrôle total, 2,9%+30¢ | Complexité, gérer la TVA soi-même |
| **GitHub Sponsors** | Intégré à l'écosystème, 0% commission | Moins adapté au modèle freemium |

> **Recommandation** : Commencer avec **LemonSqueezy** (merchant of record, gère la TVA EU automatiquement) + un système de licence basé sur une clé activée dans l'app.

---

## 👥 Stratégie communautaire {#stratégie-communautaire}

### Phase 1 — Fondation (Mois 1-2)

| Action | Détail | Priorité |
|---|---|---|
| **Serveur Discord officiel** | Hub central : #annonces, #support, #feature-requests, #memes-showcase, #dev-logs | 🔴 Critique |
| **README.md refonte** | GIF de démo en hero, badges (version, downloads, Discord), installation en 3 étapes | 🔴 Critique |
| **Page GitHub soignée** | Topics, description, social preview image, wiki avec docs | 🔴 Critique |
| **Vidéo de démo** | 60-90s montrant l'overlay en action pendant un stream | 🔴 Critique |
| **Landing page** | Site one-page avec vidéo, features, download, lien Discord (GitHub Pages ou Vercel) | 🟡 Important |

### Phase 2 — Acquisition (Mois 2-4)

| Canal | Stratégie | Objectif |
|---|---|---|
| **Reddit** | Posts sur r/Twitch, r/streaming, r/obs, r/discordapp, r/opensource avec démo GIF | 500 stars GitHub |
| **Twitter/X** | Compte @DropCastApp, clips de memes en overlay, retweets de streamers qui l'utilisent | 1000 followers |
| **YouTube** | Tutoriel setup complet (5-10 min), clips "best of memes overlay" | 5000 vues |
| **TikTok** | Clips courts de memes qui apparaissent en overlay pendant un stream | Viralité |
| **Product Hunt** | Launch day avec visuels soignés, tagline percutante | Top 5 du jour |
| **Streamers partenaires** | Offrir Pro gratuit à 10-20 petits streamers (100-500 viewers) en échange de visibilité | 10 ambassadeurs |

### Phase 3 — Rétention (Mois 4+)

| Action | Détail |
|---|---|
| **Dev logs publics** | Posts réguliers sur Discord + Twitter montrant le développement en cours |
| **Feature voting** | Système de votes pour les prochaines features (via Discord ou Canny.io) |
| **Changelog public** | Chaque release avec une note détaillée et des GIFs de démo |
| **Meme of the week** | Showcase hebdomadaire du meilleur meme overlay partagé par la communauté |
| **Programme beta** | Canal Discord #beta avec accès anticipé aux nouvelles features |
| **Open source contributions** | Label "good first issue" pour attirer des contributeurs |

### 📈 Métriques à suivre

| Métrique | Outil | Objectif M6 |
|---|---|---|
| GitHub Stars | GitHub | 1 000 |
| Downloads (Windows + Android) | GitHub Releases analytics | 5 000 |
| Membres Discord | Discord | 500 |
| Utilisateurs Pro payants | LemonSqueezy dashboard | 50 (≈250€/mois) |
| DAU (Daily Active Users) | Telemetry opt-in dans l'app | 200 |

---

## 🗺️ Roadmap par phases {#roadmap-par-phases}

### 🏁 Phase 1 — Solidification (v1.1 → v1.3) — Semaines 1-6

> Objectif : Base technique solide, premiers utilisateurs organiques

- [ ] Créer le projet `DropCast.Core` (bibliothèque partagée)
- [ ] Ajouter des tests unitaires (TrimParser, UrlDetector, TokenProvider)
- [ ] Setup GitHub Actions (build + test + release automatique)
- [ ] File d'attente de memes (FIFO, max 5)
- [ ] Animations fade-in/fade-out
- [ ] Auto-update checker (Windows)
- [ ] Serveur Discord officiel
- [ ] Refonte du README avec GIF de démo
- [ ] Vidéo de démo YouTube (60s)

### 🚀 Phase 2 — Différenciation (v1.4 → v2.0) — Semaines 7-14

> Objectif : Features uniques qui n'existent nulle part ailleurs

- [ ] Système de modération (blacklist mots/URLs, cooldown par user)
- [ ] OBS WebSocket source (browser source alternative)
- [ ] Dashboard de stats (memes affichés, top contributeurs)
- [ ] Système de vote Discord (réactions)
- [ ] Support Twitch Chat comme `IMessageSource`
- [ ] Landing page + Product Hunt launch
- [ ] Premiers partenariats streamers

### 💰 Phase 3 — Monétisation (v2.1 → v2.5) — Semaines 15-22

> Objectif : Premiers revenus, communauté active

- [ ] Implémentation du modèle Freemium (Free vs Pro)
- [ ] Intégration LemonSqueezy (licence par clé)
- [ ] Templates d'overlay marketplace
- [ ] Soundboard avec packs
- [ ] NSFW filter automatique (modération Pro)
- [ ] Telemetry opt-in (métriques d'usage anonymes)
- [ ] Programme ambassadeurs streamers

### 🌍 Phase 4 — Expansion (v3.0+) — Mois 6+

> Objectif : Multi-plateforme, multi-langue, revenus stables

- [ ] Migration Windows vers .NET 8+ (WinForms moderne ou WPF)
- [ ] Internationalisation (EN, FR, ES, DE, PT)
- [ ] Client web (Blazor WebAssembly) pour macOS/Linux
- [ ] Slash commands Discord bot (`/meme`, `/skip`, `/stats`)
- [ ] API publique pour intégrations tierces
- [ ] Multi-écran (Windows)
- [ ] Mode replay + export clip

---

## 🎯 Quick Wins — Actions immédiates (cette semaine)

| # | Action | Effort | Impact |
|---|---|---|---|
| 1 | Créer le serveur Discord officiel | 1h | 🔴 Très élevé |
| 2 | Refaire le README.md avec GIF de démo | 2h | 🔴 Très élevé |
| 3 | Ajouter une GitHub Action de build | 1h | 🟡 Élevé |
| 4 | Post Reddit r/Twitch + r/streaming | 30min | 🟡 Élevé |
| 5 | Créer le compte Twitter @DropCastApp | 30min | 🟡 Élevé |
| 6 | Ajouter un CONTRIBUTING.md | 30min | 🟢 Moyen |
| 7 | Labelliser les issues GitHub "good first issue" | 30min | 🟢 Moyen |

---

## 🏷️ Positionnement & Tagline

> **Tagline** : *"Your chat's memes, on your screen."*
> 
> **Pitch (1 phrase)** : DropCast transforme n'importe quel canal Discord en overlay de memes transparent — les viewers envoient des images, vidéos et texte qui apparaissent directement à l'écran du streamer.
> 
> **Positionnement concurrentiel** : Il n'existe aucun outil gratuit et open-source qui fait exactement ça. Les alternatives (Streamlabs, StreamElements) sont lourdes, fermées, et ne permettent pas ce niveau de contrôle communautaire.

---

## 🔑 Avantages concurrentiels à protéger

1. **Open source** — crée la confiance, attire les contributeurs
2. **Léger** — pas d'Electron, pas de navigateur embarqué (sauf OBS source)
3. **Multi-plateforme** — Windows + Android dès le jour 1
4. **Résolution multi-source** — YouTube, TikTok, Instagram, Twitter, Reddit en un seul outil
5. **Syntaxe de trimming** — unique, puissante, intuitive
6. **Chiffrement du token** — sécurité dès la v1
