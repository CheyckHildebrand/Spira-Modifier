Spira Modifier is a data editor for **Final Fantasy X HD Remaster**. It opens an extracted game folder, detects the available files, lets users edit their data through a WPF interface, and writes only modified files to a separate output folder.

The application is primarily intended for hardmod creators and users who want to modify monsters, attacks, commands, items, gear, abilities, characters, chests, and battle scenes without editing binary files by hand.

## Current version

The current version is **Spira Modifier v0.74**.

This edition includes:

- a complete **French and English** interface;
- separate selectors for the **interface language** and **file language**;
- support for appending entries to `monmagic1.bin`, `monmagic2.bin`, and `command.bin`;
- mechanic copy and paste operations that preserve localized text;
- ATEL analysis, deterministic command replacement, and validated LLM patch pretesting;
- encounter, formation, and position editing for battle scenes;
- non-destructive saving to a mirrored output folder structure.

The **Interface language** selector translates the application. The **File language** selector chooses which localized game files are displayed and edited. It does not automatically translate game text.

## Requirements

- Windows and the distributed `SpiraModifier.exe` application.
- A **clean, complete, and unmodified vanilla VBF extraction** of Final Fantasy X HD Remaster, generally organized under an `ffx_ps2` root directory. It must be configured as the vanilla reference folder.
- The extracted hardmod or project folder that will be edited.
- A backup of the game data and the mod being edited.

Spira Modifier does not include game files or a VBF extraction tool.

### Required vanilla reference folder

Spira Modifier requires a **clean extracted vanilla VBF** to work reliably. Keep its `ffx_ps2` folder unmodified. The application uses it as a reference source for character sets, kernels, monster files, and other data that may be missing or incomplete in the opened hardmod.

Configure it once through **File > Configure the reference vanilla folder...**. The setting is remembered and reused the next time the application starts.

The vanilla reference folder must not be the hardmod folder or the output folder. Never modify its contents directly.

## Basic usage

1. Start `SpiraModifier.exe`.
2. On first use, open **File > Configure the reference vanilla folder...**.
3. Select the `ffx_ps2` folder from the clean and unmodified vanilla VBF extraction.
4. Then open **File > Open an extracted folder...**.
5. Select the hardmod or project root that will be edited, usually its `ffx_ps2` folder.
6. Review the **Scan report** tab to confirm the detected files, languages, and fallbacks.
7. Choose the UI language from **Interface language**.
8. Choose the localization to edit from **File language**.
9. Open a tab, select an entry, and change the required fields.
10. Click the relevant **Apply** button. The change is now stored in memory but has not yet been written to disk.
11. Press **Ctrl+S** or use **File > Save** to select an output folder and write the modified files.
12. Review the generated files and test them in game before permanently integrating them into a mod.

### Apply, revert, and save

- **Apply** validates the visible fields and marks the corresponding in-memory file as modified.
- **Revert** restores the values currently loaded in memory for the selected section.
- **Ctrl+S** writes every pending modification to the configured output folder.
- **Ctrl+Shift+S** selects a new output folder.

The output folder must be different from the source folder. Spira Modifier reproduces the relative folder structure there and never overwrites the original source files directly.

### Optional external character set

In addition to the required vanilla reference folder, the **File** menu can configure an external `ffx_encoding` folder. It is used to decode and encode text when character sets are available in neither the hardmod nor the vanilla reference.

## Available features

### General features

- Automatic scanning and capability-based tab activation.
- Detailed reporting of kernels, languages, character sets, detected files, and warnings.
- French or English interface language, switchable at any time.
- Detection of `frpc`, `enpc`, `uspc`, `depc`, `espc`, `sppc`, `itpc`, `chpc`, `cnpc`, `krpc`, and `jppc` localizations when present in the extraction.
- Independent game-text file-language selection.
- Name, ID, category, character, source, and type filters where applicable.
- Decimal and `0x...` hexadecimal numeric input in the relevant editors.
- Apply, revert, and visual in-memory modification tracking.
- Saving of every modified file to an external folder while preserving the relative directory structure.
- Round-trip testing for `monsterN.bin` localization files without intentionally changing their text.

### Monsters

Main files: individual `battle/mon/m*.bin` files and localized `monster1.bin`, `monster2.bin`, and `monster3.bin` databases.

- Search by file ID or decoded name.
- Localized editing of the name, Sensor text, short Sensor text, Scan text, and short Scan text.
- Support for FFX control tokens such as line breaks, colors, pauses, variables, and character references.
- Quick insertion controls for common text tokens.
- Editing of gil, normal and overkill AP, Kimahri's Ronso Rage ability, and arena price.
- Editing of primary and secondary drops: normal, overkill, common, and rare item/quantity variants.
- Editing of common and rare steals, steal chance, bribe, and stolen gil.
- Gear-drop editing: chance, slot count, ability count, and forced weapon or armor abilities.
- Editing of HP, MP, overkill threshold, Strength, Defense, Magic, Magic Defense, Agility, Luck, Evasion, Accuracy, and poison damage.
- Editing of elemental absorb, immunity, resistance, and weakness properties.
- Editing of status resistances and permanent, temporary, and extra auto-statuses.
- Editing of extra immunities and enemy-exclusive special immunities.
- Editing of battle metadata: forced action, monster/model/arena IDs, Doom counter, sound bank, and CTB icon.
- Copying and pasting mechanics between monsters without copying text or localized IDs.
- Display of the 16 command slots and internal file structure.

The monster command list is currently read-only.

### Monster attacks

Files: `monmagic1.bin` and `monmagic2.bin`.

- Combined or source-specific attack lists.
- Search by global ID or localized name.
- Append an entry to `monmagic1.bin` by cloning template `0x4000`.
- Append an entry to `monmagic2.bin` by cloning template `0x6000`.
- Localized editing of full name, short name, description, and short description.
- Editing of power, accuracy, hit count, formula, MP/Overdrive costs, critical bonus, shatter chance, and move rank.
- Editing of animations, icon, user, allowed targets, and advanced bytes.
- Decoded editing of advanced flags, damage type, targeting, and elements.
- Editing of inflicted statuses with chances and durations.
- Editing of special effects, stat buffs, and Overdrive parameters.
- Copying and pasting mechanics without changing text.
- Applying the current entry's mechanics to every loaded language.
- Copying all mechanics from the current file language to the other loaded languages.

### Monster AI and ATEL

- Monster file selection and refresh.
- ATEL bytecode decompilation into a readable listing.
- Worker, function, jump, variable, and annotation display.
- Natural-language heuristic analysis of detected actions, reactions, conditions, phases, cameras, and effects.
- Read-only raw listing view.
- Local copilot with shortcuts for summaries, actions, counters, and modification plans.
- Indexing of all available monster files to provide global examples to the copilot.
- Deterministic replacement of one ATEL command ID with another, with an internal pretest before application.
- Optional connection to a Chat Completions-compatible endpoint and configurable model.
- LLM connection testing; the entered API key is not stored.
- Optional LLM generation of a structured ATEL JSON patch, followed by validation, pretesting, and user confirmation before in-memory application.

Version 0.73 does not yet provide a free-form graphical editor or general compiler for arbitrarily rewriting the whole AI. Analysis and listings remain read-only; only controlled patch operations are applied.

### Battle scenes and encounters

Files: `btl.bin` encounter tables and individual `battle/btl/{map}/{scene}.bin` scenes.

- Area list with readable names and French or English search.
- Filters for all areas, random encounters, scripted encounters, or mixed areas.
- Display of internal code, table ID, group count, and formation count.
- Random-group display with danger, battlefield, weight, and battle-file information.
- Random encounter percentage editing with an enforced 100% total.
- Scripted-group display with direct links to their battle scenes.
- Formation monster, raw ID, and position display.
- Player-character and Aeon position display.
- Underwater battle, common voice-line, area-count, and scene ATEL-size information.
- Editing of the eight formation slots and their upper flag bits.
- Editing of monster X, Y, Z, and W coordinates.
- Editing of underwater-battle and common-voice-line options.
- Display of engine limits: 8 formation slots, 4 simultaneously active monsters, 3 player characters, and 7 Aeons.

Scene-specific ATEL bytecode is not yet decompiled or editable; only its size is displayed.

### Player and Aeon commands

File: `command.bin`.

- Category, character, and name filters.
- Append an entry by cloning template `0x3000`.
- Localized editing of full name, short name, description, and short description.
- Editing of damage mechanics, costs, critical properties, animations, owner, and targets.
- Editing of advanced flags, elements, statuses, durations, special effects, and buffs.
- Editing of the `command.bin` extension: menu order, Sphere Grid type, and extra bytes.
- Copying and pasting mechanics without changing text.
- Applying the current entry or every entry to the other loaded languages.

### Items

File: `item.bin`.

- Search and localized editing of full name, short name, description, and short description.
- Editing of power, accuracy, target count, formula, costs, critical properties, and shatter chance.
- Editing of animations, targets, advanced bytes, and flags.
- Editing of effect type, targeting, elements, statuses, chances, and durations.
- Editing of special effects and stat buffs.
- Copying and pasting mechanics without changing text.
- Applying the current item or every item to the other loaded languages.

### Gear

Files: `weapon.bin`, `buki_get.bin`, `shop_arms.bin`, `w_name.bin`, and `a_ability.bin` references.

- Source selection: starting gear, drops/chests, shops, or all sources.
- Weapon/armor, character/Aeon, ID, and ability filters.
- Human-character weapon-name resolution through `w_name.bin`.
- Editing of full and short names and the `w_name` model.
- Editing of slots, formula, power, critical rate, model, armor byte, flags, and name indexes.
- Editing of four ability slots with `a_ability.bin` name resolution.
- Editing of decoded gear flags.
- Applying weapon names to every loaded `w_name.bin` language file.

### Gear abilities

Files: `a_ability.bin` and `kaizou.bin` customization recipes.

- Filters for weapon-only, armor-only, shared, or recipe-less abilities.
- Localized editing of full and short names and descriptions.
- Metadata editing: icon, group, level, stat bonus, bonus target, and International bonus.
- Separate weapon and armor recipe editing: required item and quantity.
- Editing of elemental effects, auto-statuses, inflicted statuses, and resistances.
- Editing of raw flags and extra fields.
- Applying ability mechanics to the other loaded languages.

### Player starting data

Files: `ply_save.bin` and `weapon.bin` gear references.

- Editing of base HP/MP and the eight primary stats.
- Editing of starting state: current and maximum HP/MP, AP, poison, current stats, and Overdrive.
- Starting weapon and armor selection.
- Editing of available/used Sphere Levels and raw flags.
- Editing of initially learned abilities.
- Applying mechanical data to every loaded `ply_save.bin` without replacing localized names.

Starting inventory data is not exposed by the parser and is unavailable in this version.

### Maps and chests

Files: `takara.bin` and `.ebp` event scripts used for reference discovery.

- Chest-entry listing and search by map, event, index, or contents.
- Gil, item, gear, key-item, referenced, and unreferenced filters.
- Detected-map filter.
- Display of each entry's detected map and event usages.
- Editing of content type, quantity, target, and raw values.
- Resolution of available items, gear entries, and key items.
- Apply and revert support for `takara.bin` changes.

### Scan report

- Summary of detected files and capabilities.
- Kernel details for each available language.
- Status of name databases, character sets, attacks, commands, items, and other tables.
- Warnings for missing files and applied fallbacks.

## Current limitations

- The **Sphere Grid** tab is a preview of a future module and is not editable yet.
- Commands embedded in monster files are displayed as read-only data.
- Battle-scene ATEL is not yet decompiled.
- Player starting inventory is not editable.
- The ATEL editor is not yet a complete free-form programming or recompilation environment.
- Tabs remain disabled when their required file is absent from both the opened folder and the configured vanilla reference folder.

## Main supported files

| Area | Main files |
| --- | --- |
| Monsters | `battle/mon/m*.bin`, `monster1.bin`, `monster2.bin`, `monster3.bin` |
| Attacks | `monmagic1.bin`, `monmagic2.bin` |
| Commands | `command.bin` |
| Items | `item.bin`, `important.bin` |
| Gear | `weapon.bin`, `buki_get.bin`, `shop_arms.bin`, `w_name.bin` |
| Abilities | `a_ability.bin`, `kaizou.bin` |
| Characters | `ply_save.bin` |
| Chests | `takara.bin`, `.ebp` scripts for references |
| Encounters | `btl.bin` |
| Battle scenes | `battle/btl/{map}/*.bin` |
| Text | character tables from `ffx_encoding` |

## Safety recommendations

- Always work on a copied game extraction.
- Use an empty output folder that is separate from the source folder.
- Review the save report before copying files into the game installation.
- Test changes incrementally in game, especially ATEL, advanced flags, and battle formations.
- Always keep the clean vanilla VBF extraction as a separate reference and never use it as a working or output folder.
