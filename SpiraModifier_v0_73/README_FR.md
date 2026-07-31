# Spira Modifier v0.73

> Documentation française. [Read the English documentation](README_EN.md).

Spira Modifier est un éditeur de données pour **Final Fantasy X HD Remaster**. Il ouvre une extraction du jeu, détecte les fichiers disponibles, permet de modifier leurs données dans une interface WPF, puis écrit uniquement les fichiers modifiés dans un dossier de sortie séparé.

Le logiciel s'adresse principalement aux créateurs de hardmods et aux utilisateurs qui souhaitent modifier les monstres, attaques, commandes, objets, équipements, aptitudes, personnages, coffres et scènes de combat sans éditer manuellement les fichiers binaires.

## Version actuelle

La version actuelle est **Spira Modifier v0.73**.

Cette édition comprend notamment :

- une interface entièrement disponible en **français et en anglais** ;
- un sélecteur séparé pour la **langue du logiciel** et la **langue des fichiers** ;
- l'ajout de nouvelles entrées dans `monmagic1.bin`, `monmagic2.bin` et `command.bin` ;
- la copie et le collage des mécaniques sans écraser les textes localisés ;
- l'analyse ATEL, le remplacement déterministe de commandes et le prétest de patchs LLM ;
- l'édition des rencontres, formations et positions des scènes de combat ;
- la sauvegarde non destructive dans une arborescence de sortie.

Le choix **Langue du logiciel** traduit l'interface. Le choix **Langue des fichiers** sélectionne les fichiers localisés à afficher et à modifier. Il ne traduit pas automatiquement les textes du jeu.

## Prérequis

- Windows et la version distribuée de `SpiraModifier.exe`.
- Une extraction VBF **vanilla, propre, complète et non modifiée** de Final Fantasy X HD Remaster, généralement organisée sous un dossier racine `ffx_ps2`. Elle doit être configurée comme dossier vanilla de référence.
- Le dossier extrait du hardmod ou du projet à modifier.
- Une copie de sauvegarde de vos données de jeu et de votre mod.

Spira Modifier ne fournit ni les fichiers du jeu ni l'outil d'extraction de l'archive VBF.

### Dossier vanilla de référence obligatoire

Pour que Spira Modifier fonctionne correctement, vous devez disposer d'un **VBF vanilla extrait propre** et conserver son dossier `ffx_ps2` sans modification. Ce dossier sert de référence pour récupérer les charsets, kernels, fichiers monstres et autres données absentes ou incomplètes dans le hardmod ouvert.

Configurez-le une première fois avec **Fichier > Configurer le dossier vanilla de référence...**. Le réglage est mémorisé et réutilisé aux ouvertures suivantes.

Le dossier vanilla de référence ne doit pas être le dossier du hardmod et ne doit pas servir de dossier de sortie. Ne modifiez jamais directement son contenu.

## Utilisation de base

1. Lancez `SpiraModifier.exe`.
2. Lors de la première utilisation, ouvrez **Fichier > Configurer le dossier vanilla de référence...**.
3. Sélectionnez le dossier `ffx_ps2` provenant de votre extraction VBF vanilla propre et non modifiée.
4. Ouvrez ensuite **Fichier > Ouvrir un dossier extrait...**.
5. Sélectionnez la racine du hardmod ou du projet à modifier, généralement son dossier `ffx_ps2`.
6. Consultez l'onglet **Rapport de scan** pour vérifier les fichiers, langues et fallbacks détectés.
7. Choisissez la langue de l'interface dans **Langue du logiciel**.
8. Choisissez la localisation à éditer dans **Langue des fichiers**.
9. Ouvrez un onglet, sélectionnez une entrée et modifiez les champs souhaités.
10. Cliquez sur le bouton **Appliquer** de la section concernée. La modification est alors enregistrée en mémoire, mais pas encore écrite sur le disque.
11. Utilisez **Ctrl+S** ou **Fichier > Sauvegarder** pour choisir un dossier de sortie et écrire les fichiers modifiés.
12. Vérifiez les fichiers générés et testez-les en jeu avant de les intégrer définitivement à votre mod.

### Appliquer, annuler et sauvegarder

- **Appliquer** valide les champs de l'écran et marque le fichier correspondant comme modifié en mémoire.
- **Annuler** restaure les valeurs actuellement chargées en mémoire pour la section sélectionnée.
- **Ctrl+S** écrit toutes les modifications en attente dans le dossier de sortie configuré.
- **Ctrl+Maj+S** choisit un nouveau dossier de sortie.

Le dossier de sortie doit être différent du dossier source. Spira Modifier y reproduit l'arborescence relative des fichiers modifiés et ne remplace jamais directement les originaux.

### Charset externe optionnelle

En plus du dossier vanilla de référence requis, le menu **Fichier** permet de configurer un dossier `ffx_encoding` externe. Il sera utilisé pour décoder et réencoder les textes si les charsets ne sont disponibles ni dans le hardmod ni dans la référence vanilla.

## Fonctionnalités disponibles

### Fonctions générales

- Scan automatique des fichiers disponibles et activation des onglets compatibles.
- Rapport détaillé des kernels, langues, charsets, fichiers trouvés et avertissements.
- Interface française ou anglaise, modifiable à tout moment.
- Détection des localisations `frpc`, `enpc`, `uspc`, `depc`, `espc`, `sppc`, `itpc`, `chpc`, `cnpc`, `krpc` et `jppc` lorsqu'elles existent dans l'extraction.
- Sélection indépendante de la langue des textes du jeu.
- Filtres par nom, ID, catégorie, personnage, source ou type selon les onglets.
- Acceptation des valeurs numériques décimales ou hexadécimales `0x...` dans les éditeurs concernés.
- Application, annulation et suivi visuel des modifications en mémoire.
- Sauvegarde de tous les fichiers modifiés dans un dossier externe avec conservation de l'arborescence.
- Test de round-trip des fichiers de localisation `monsterN.bin`, sans modification volontaire des textes.

### Monstres

Fichiers principaux : fichiers individuels `battle/mon/m*.bin` et bases localisées `monster1.bin`, `monster2.bin`, `monster3.bin`.

- Recherche par ID de fichier ou nom décodé.
- Édition localisée du nom, du texte Sensor, du Sensor court, du Scan et du Scan court.
- Prise en charge des tokens FFX tels que les sauts de ligne, couleurs, pauses, variables et personnages.
- Barre d'insertion rapide pour les principaux tokens de texte.
- Édition des gils, AP normaux et overkill, Émulattaque de Kimahri et prix d'arène.
- Édition des drops principal et secondaire : variantes normales, overkill, communes et rares, avec objets et quantités.
- Édition du vol commun et rare, de la chance de vol, du pot-de-vin et des gils volés.
- Édition du drop d'équipement : chance, nombre de slots, nombre d'aptitudes et aptitudes forcées d'arme ou de protection.
- Édition des HP, MP, seuil d'overkill, Force, Défense, Magie, Défense magique, Agilité, Chance, Esquive, Précision et dégâts de poison.
- Édition des affinités élémentaires : absorption, immunité, résistance et faiblesse.
- Édition des résistances aux statuts, auto-statuts permanents, temporaires et supplémentaires.
- Édition des immunités supplémentaires et des immunités spéciales réservées aux ennemis.
- Édition des métadonnées de combat : action forcée, IDs monstre/modèle/arène, Doom, banque audio et icône CTB.
- Copie et collage des mécaniques d'un monstre vers un autre sans copier les textes ni l'identifiant localisé.
- Affichage des 16 slots de commandes et de la structure interne du fichier.

La liste des commandes du monstre est actuellement affichée en lecture seule.

### Attaques de monstres

Fichiers : `monmagic1.bin` et `monmagic2.bin`.

- Affichage combiné ou filtré par fichier source.
- Recherche par ID global ou nom localisé.
- Ajout d'une entrée dans `monmagic1.bin` par clonage du modèle `0x4000`.
- Ajout d'une entrée dans `monmagic2.bin` par clonage du modèle `0x6000`.
- Édition localisée du nom complet, nom court, description et description courte.
- Édition de la puissance, précision, nombre de coups, formule, coûts MP/OD, critique, pétrification massive et rang de mouvement.
- Édition des animations, icône, utilisateur, cibles permises et octets avancés.
- Édition décodée des flags avancés, du type de dégâts, du ciblage et des éléments.
- Édition des statuts infligés avec chances et durées.
- Édition des effets spéciaux, buffs de statistiques et paramètres d'Overdrive.
- Copie et collage des mécaniques sans modifier les textes.
- Application des mécaniques de l'entrée courante à toutes les langues chargées.
- Copie des mécaniques de toutes les entrées de la langue courante vers les autres langues.

### IA des monstres et ATEL

- Sélection et rafraîchissement d'un fichier monstre.
- Décompilation du bytecode ATEL en listing lisible.
- Affichage des workers, fonctions, sauts, variables et annotations.
- Analyse heuristique en langage naturel des actions, réactions, conditions, phases, caméras et effets détectés.
- Consultation du listing brut en lecture seule.
- Copilote local avec raccourcis pour le résumé, les actions, les counters et le plan de modification.
- Indexation de tous les fichiers monstres disponibles afin de fournir des exemples globaux au copilote.
- Remplacement déterministe d'un ID de commande ATEL par un autre, avec prétest interne avant application.
- Connexion optionnelle à un endpoint compatible Chat Completions avec modèle configurable.
- Test de la connexion LLM ; la clé API saisie n'est pas enregistrée.
- Génération optionnelle d'un patch ATEL JSON par le LLM, validation structurée, prétest et demande de confirmation avant application en mémoire.

La v0.73 ne propose pas encore un éditeur graphique ou un compilateur libre pour réécrire arbitrairement toute l'IA. L'analyse et le listing restent en lecture seule ; seules les opérations de patch contrôlées sont appliquées.

### Scènes de combat et rencontres

Fichiers : tables `btl.bin` et scènes individuelles `battle/btl/{map}/{scene}.bin`.

- Liste des zones avec noms lisibles et recherche en français ou en anglais.
- Filtres : toutes les zones, rencontres aléatoires, rencontres scriptées ou zones mixtes.
- Affichage du code interne, de l'ID de table, du nombre de groupes et de formations.
- Affichage des groupes aléatoires avec danger, battlefield, poids et fichier de bataille.
- Édition des pourcentages de rencontre aléatoire avec validation d'un total de 100 %.
- Affichage des groupes scriptés et accès direct à leurs scènes.
- Affichage des monstres de la formation, IDs bruts et positions.
- Affichage des positions des personnages et des Chimères.
- Affichage du combat sous-marin, des lignes de voix communes, des zones et de la taille du script ATEL de scène.
- Édition des 8 slots de formation et des flags hauts associés.
- Édition des coordonnées X, Y, Z et W des positions de monstres.
- Édition des options combat sous-marin et lignes de voix communes.
- Contrôle des limites moteur affichées par l'interface : 8 slots de formation, 4 monstres actifs simultanément, 3 personnages et 7 Chimères.

Le bytecode ATEL propre aux scènes n'est pas encore décompilé ni édité ; seule sa taille est affichée.

### Commandes des personnages et Chimères

Fichier : `command.bin`.

- Filtres par catégorie, personnage et nom.
- Ajout d'une entrée par clonage du modèle `0x3000`.
- Édition localisée du nom, nom court, description et description courte.
- Édition des mécaniques de dégâts, coûts, critique, animations, propriétaire et cibles.
- Édition des flags avancés, éléments, statuts, durées, effets spéciaux et buffs.
- Édition de l'extension `command.bin` : ordre de menu, type de sphérier et octets supplémentaires.
- Copie et collage des mécaniques sans modifier les textes.
- Application de l'entrée courante ou de toutes les entrées aux autres langues chargées.

### Objets

Fichier : `item.bin`.

- Recherche et édition localisée du nom, nom court, description et description courte.
- Édition de la puissance, précision, nombre de cibles, formule, coûts, critique et pétrification massive.
- Édition des animations, cibles, octets et flags avancés.
- Édition du type d'effet, du ciblage, des éléments, statuts, chances et durées.
- Édition des effets spéciaux et buffs de statistiques.
- Copie et collage des mécaniques sans modifier les textes.
- Application de l'objet courant ou de tous les objets aux autres langues chargées.

### Équipements

Fichiers : `weapon.bin`, `buki_get.bin`, `shop_arms.bin`, `w_name.bin` et références `a_ability.bin`.

- Sélection de la source : équipement de départ, drops/coffres, boutique ou ensemble des sources.
- Filtres par arme/protection, personnage/Chimère, ID ou aptitude.
- Résolution et affichage des noms d'armes via `w_name.bin` pour les personnages humains.
- Édition des noms complet et court ainsi que du modèle `w_name`.
- Édition des slots, formule, puissance, critique, modèle, octet d'armure, flags et index de nom.
- Édition des quatre slots d'aptitudes avec résolution via `a_ability.bin`.
- Édition des drapeaux décodés de l'équipement.
- Application des noms d'armes dans tous les `w_name.bin` chargés.

### Aptitudes d'équipement

Fichiers : `a_ability.bin` et recettes `kaizou.bin`.

- Filtres par aptitudes d'arme, de protection, communes ou sans recette de customisation.
- Édition localisée des noms et descriptions complets et courts.
- Édition des métadonnées : icône, groupe, niveau, bonus de statistique, cible du bonus et bonus International.
- Édition séparée des recettes d'arme et de protection : objet requis et quantité.
- Édition des effets élémentaires, auto-statuts, statuts infligés et résistances.
- Édition des flags bruts et champs supplémentaires.
- Application des mécaniques de l'aptitude aux autres langues chargées.

### Données de départ des personnages

Fichiers : `ply_save.bin` et références d'équipement `weapon.bin`.

- Édition des statistiques de base HP/MP et des huit statistiques principales.
- Édition de l'état de départ : HP/MP actuels et maximums, AP, poison, statistiques courantes et Overdrive.
- Sélection de l'arme et de la protection équipées.
- Édition des niveaux de sphères disponibles/utilisés et des flags bruts.
- Édition des compétences apprises au départ.
- Application des données mécaniques à tous les `ply_save.bin` chargés sans remplacer les noms localisés.

L'inventaire de départ n'est pas encore exposé par le parser et reste indisponible dans cette version.

### Maps et coffres

Fichiers : `takara.bin` et scripts d'événements `.ebp` utilisés pour retrouver les références.

- Liste et recherche des entrées de coffres par map, événement, index ou contenu.
- Filtres par gils, objets, équipements, objets clés, entrées référencées ou non référencées.
- Filtre par map détectée.
- Affichage des utilisations de chaque entrée dans les maps et événements.
- Édition du type de contenu, de la quantité, de la cible et des valeurs brutes.
- Résolution des objets, équipements et objets clés disponibles.
- Application et annulation des modifications de `takara.bin`.

### Rapport de scan

- Résumé des fichiers et fonctionnalités détectés.
- Détail des kernels disponibles par langue.
- État des bases de noms, charsets, attaques, commandes, objets et autres tables.
- Avertissements sur les fichiers manquants ou les fallbacks utilisés.

## Limites actuelles

- L'onglet **Sphérier** est un aperçu de module futur et ne permet pas encore l'édition.
- Les commandes intégrées aux fichiers monstres sont affichées en lecture seule.
- L'ATEL des scènes de combat n'est pas encore décompilé.
- L'inventaire de départ des personnages n'est pas éditable.
- L'éditeur ATEL ne remplace pas encore un environnement complet de programmation ou de recompilation libre.
- Certains onglets restent désactivés lorsqu'un fichier requis n'est pas présent dans le dossier ouvert ou dans le dossier vanilla de référence.

## Principaux fichiers pris en charge

| Domaine | Fichiers principaux |
| --- | --- |
| Monstres | `battle/mon/m*.bin`, `monster1.bin`, `monster2.bin`, `monster3.bin` |
| Attaques | `monmagic1.bin`, `monmagic2.bin` |
| Commandes | `command.bin` |
| Objets | `item.bin`, `important.bin` |
| Équipements | `weapon.bin`, `buki_get.bin`, `shop_arms.bin`, `w_name.bin` |
| Aptitudes | `a_ability.bin`, `kaizou.bin` |
| Personnages | `ply_save.bin` |
| Coffres | `takara.bin`, scripts `.ebp` pour les références |
| Rencontres | `btl.bin` |
| Scènes de combat | `battle/btl/{map}/*.bin` |
| Textes | tables de caractères du dossier `ffx_encoding` |

## Recommandations de sécurité

- Travaillez toujours sur une copie extraite du jeu.
- Utilisez un dossier de sortie vide et séparé du dossier source.
- Vérifiez le rapport de sauvegarde avant de copier les fichiers dans votre installation.
- Testez les modifications en jeu progressivement, surtout pour l'ATEL, les flags avancés et les formations de combat.
- Conservez toujours votre extraction VBF vanilla propre comme référence séparée et ne l'utilisez jamais comme dossier de travail ou de sortie.
