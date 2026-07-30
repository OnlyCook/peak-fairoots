#!/usr/bin/env bash
#
# apply-presets.sh — turn docs/PRESETS.md into the C# the mod reads.
#
# docs/PRESETS.md is the source of truth for every number in Fairoots: the
# default each config entry is bound with, and what each of the four presets
# sets it to. This script parses those tables and (re)generates:
#
#   src/Fairoots/Core/ConfigDefaults.g.cs        <- the "Default" column
#   src/Fairoots/Core/Presets/PresetValues.g.cs  <- the four preset columns
#
# Usage:
#   bash scripts/apply-presets.sh            # regenerate both files
#   bash scripts/apply-presets.sh --check    # verify only; change nothing.
#                                            # Non-zero exit if the checked-in
#                                            # files are stale or a setting has
#                                            # drifted out of the table.
#
# The tuning loop is: edit a cell in docs/PRESETS.md, run this, then
#   cd src/Fairoots && dotnet build -c Release -p:DeployToProfile=true
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
table="$repo_root/docs/PRESETS.md"
defaults_out="$repo_root/src/Fairoots/Core/ConfigDefaults.g.cs"
values_out="$repo_root/src/Fairoots/Core/Presets/PresetValues.g.cs"
plugin_config="$repo_root/src/Fairoots/PluginConfig.cs"
preset_catalog="$repo_root/src/Fairoots/Core/Presets/PresetCatalog.cs"

check_only=0
case "${1:-}" in
    --check) check_only=1 ;;
    "") ;;
    *) echo "usage: $(basename "$0") [--check]" >&2; exit 2 ;;
esac

[[ -f $table ]] || { echo "apply-presets: missing $table" >&2; exit 1; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# --- parse ------------------------------------------------------------------
# One record per settings row: section|setting|id|type|default|subtle|balanced|
# generous|tame, with markdown noise (backticks, the "*" documented-exception
# marker, em dashes) already stripped and values already formatted as C#
# literals. Anything that isn't a settings row is dropped here rather than in
# the generators, so both of them see the same clean input.
#
# The C# identifier is not a visible column - it would be duplicated noise in a
# table meant to be read while tuning. It rides along in a trailing HTML comment
# instead (<!--WindForceMultiplier-->), which markdown renderers drop as an
# excess cell. A row without one isn't a settings row, which is also how the
# "How to read the columns" table gets skipped.
awk '
function clean(s) {
    gsub(/`/, "", s); gsub(/\*/, "", s)
    gsub(/^[ \t]+|[ \t]+$/, "", s)
    return s
}
/^## / { section = substr($0, 4); next }
/^\|/ {
    if (match($0, /<!--[A-Za-z][A-Za-z0-9]*-->/) == 0) next
    id = substr($0, RSTART + 4, RLENGTH - 7)
    n = split($0, cell, /\|/)
    # 7 visible columns, so at least 8 pipe-delimited fields before the comment.
    if (n < 9) next
    setting = clean(cell[2]); type = clean(cell[3]); def = clean(cell[4])
    s = clean(cell[5]); b = clean(cell[6]); g = clean(cell[7]); t = clean(cell[8])
    print section "|" setting "|" id "|" type "|" def "|" s "|" b "|" g "|" t
}
' "$table" > "$tmp/rows"

rows=$(wc -l < "$tmp/rows")
[[ $rows -gt 0 ]] || { echo "apply-presets: parsed no settings rows out of $table" >&2; exit 1; }

# --- validate ---------------------------------------------------------------
awk -F'|' '
function isnum(v) { return v ~ /^-?([0-9]+\.?[0-9]*|\.[0-9]+)$/ }
function isbool(v) { return v == "on" || v == "off" }
function bad(msg) { printf("apply-presets: %s (row: %s / %s)\n", msg, $2, $3) > "/dev/stderr"; errors++ }
{
    id = $3; type = $4; def = $5
    if (id !~ /^[A-Za-z][A-Za-z0-9]*$/) bad("Id \"" id "\" is not a C# identifier")
    if (id in seen) bad("duplicate Id \"" id "\"")
    seen[id] = 1
    if (type != "bool" && type != "int" && type != "double" && type != "float") \
        bad("unknown Type \"" type "\" - this file only holds generated balance values")
    if (type == "bool" && !isbool(def)) bad("bool default must be on/off, got \"" def "\"")
    if ((type == "int" || type == "double" || type == "float") && !isnum(def)) \
        bad("numeric default expected, got \"" def "\"")

    dashes = 0
    for (i = 6; i <= 9; i++) if ($i == "\xe2\x80\x94" || $i == "-" || $i == "") dashes++
    if (dashes != 0 && dashes != 4) bad("preset columns must be all filled or all \"-\"")
    if (dashes == 0) {
        for (i = 6; i <= 9; i++) {
            if (type == "bool") { if (!isbool($i)) bad("preset column " (i-5) " must be on/off, got \"" $i "\"") }
            else if (!isnum($i)) bad("preset column " (i-5) " must be numeric, got \"" $i "\"")
        }
    }
}
END { if (errors) exit 1 }
' "$tmp/rows"

# --- generate ---------------------------------------------------------------
# lit(): a C# literal of the row's type. Floats need the f suffix; bools come
# out of the table as on/off; everything else is already a valid literal.
gen_awk='
function lit(type, v) {
    if (type == "bool") return (v == "on") ? "true" : "false"
    if (type == "float") return v "f"
    # A whole number written as "30" is an int literal, which would make
    # Pick<T> infer int and stop compiling against a double method.
    if (type == "double" && v !~ /\./) return v ".0"
    return v
}
function csharp_type(type) { return type }
'

cat > "$tmp/defaults.cs" <<'HEADER'
// <auto-generated>
//     Generated from docs/PRESETS.md by scripts/apply-presets.sh - do not edit.
//     Change a value in docs/PRESETS.md's "Default" column and re-run the
//     script; hand edits here are overwritten and CI's --check fails on them.
// </auto-generated>

namespace Fairoots.Core
{
    /// <summary>
    /// The value every Fairoots <em>balance</em> config entry is bound with -
    /// every setting in the five gameplay sections. Generated from
    /// docs/PRESETS.md, which documents the rule these encode: <b>every default
    /// is the vanilla value</b>, so Custom-preset-plus-untouched-settings plays
    /// exactly like unmodded PEAK. The one documented exception, gated parameters,
    /// is marked in the table. <c>General</c> and <c>Debug</c> are not balance
    /// values and are not generated - those defaults are literals in
    /// <c>PluginConfig</c>.
    /// </summary>
    public static class ConfigDefaults
    {
HEADER

awk -F'|' "$gen_awk"'
$4 != "bool" && $4 != "int" && $4 != "double" && $4 != "float" { next }
$1 != section { section = $1; printf("%s        // --- %s ---\n", (first++ ? "\n" : ""), section) }
{ printf("        public const %s %s = %s;\n", csharp_type($4), $3, lit($4, $5)) }
' "$tmp/rows" >> "$tmp/defaults.cs"

cat >> "$tmp/defaults.cs" <<'FOOTER'
    }
}
FOOTER

cat > "$tmp/values.cs" <<'HEADER'
// <auto-generated>
//     Generated from docs/PRESETS.md by scripts/apply-presets.sh - do not edit.
//     Change a preset column in docs/PRESETS.md and re-run the script; hand
//     edits here are overwritten and CI's --check fails on them.
// </auto-generated>

using System;

namespace Fairoots.Core.Presets
{
    /// <summary>
    /// The raw per-preset numbers, one method per setting, indexed by presets
    /// 1-4 only. <see cref="PresetCatalog"/> is the documented front door onto
    /// this - it maps <see cref="PresetId.Custom"/> to a safe key first and is
    /// where the reasoning for each row lives. Game-facing code should read
    /// neither directly: it wants <c>PluginConfig</c>'s <c>Effective*</c>
    /// accessors, which fold in the player's override and host authority.
    /// </summary>
    public static class PresetValues
    {
        private static T Pick<T>(PresetId preset, T subtle, T balanced, T generous, T tame)
        {
            switch (preset)
            {
                case PresetId.Subtle: return subtle;
                case PresetId.Balanced: return balanced;
                case PresetId.Generous: return generous;
                case PresetId.Tame: return tame;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(preset),
                        preset,
                        "PresetValues is indexed by presets 1-4 only - map Custom first (PresetCatalog does).");
            }
        }
HEADER

awk -F'|' "$gen_awk"'
{
    dashes = 0
    for (i = 6; i <= 9; i++) if ($i == "\xe2\x80\x94" || $i == "-" || $i == "") dashes++
    if (dashes == 4) next
    if ($1 != section) { section = $1; printf("\n        // --- %s ---\n", section) }
    printf("        public static %s %s(PresetId preset) =>\n            Pick(preset, %s, %s, %s, %s);\n",
           csharp_type($4), $3, lit($4, $6), lit($4, $7), lit($4, $8), lit($4, $9))
}
' "$tmp/rows" >> "$tmp/values.cs"

cat >> "$tmp/values.cs" <<'FOOTER'
    }
}
FOOTER

# --- cross-check the generated ids against the code that must consume them ---
# Catches the failure mode the generator can't: a row exists in the table and
# in the generated file, but PluginConfig still binds a hardcoded literal (or
# PresetCatalog still hardcodes a switch), so editing the table silently does
# nothing. Advisory while regenerating, fatal under --check.
missing=""
while IFS='|' read -r _section _setting id type _def s _b _g _t; do
    case $type in bool|int|double|float) ;; *) continue ;; esac
    grep -q "ConfigDefaults\.$id\b" "$plugin_config" || missing+="  PluginConfig.cs does not bind ConfigDefaults.$id"$'\n'
    case $s in "—"|"-"|"") continue ;; esac
    grep -q "PresetValues\.$id\b" "$preset_catalog" || missing+="  PresetCatalog.cs does not read PresetValues.$id"$'\n'
done < "$tmp/rows"

# --- write or verify --------------------------------------------------------
stale=0
for pair in "$tmp/defaults.cs:$defaults_out" "$tmp/values.cs:$values_out"; do
    src=${pair%%:*}; dst=${pair#*:}
    if [[ $check_only -eq 1 ]]; then
        if ! cmp -s "$src" "$dst"; then
            echo "apply-presets: $(realpath --relative-to="$repo_root" "$dst") is stale (re-run scripts/apply-presets.sh)" >&2
            stale=1
        fi
    else
        mkdir -p "$(dirname "$dst")"
        cp "$src" "$dst"
    fi
done

if [[ -n $missing ]]; then
    if [[ $check_only -eq 1 ]]; then
        echo "apply-presets: settings in docs/PRESETS.md that no code reads:" >&2
        printf '%s' "$missing" >&2
        stale=1
    else
        echo "apply-presets: warning - settings in docs/PRESETS.md that no code reads yet:" >&2
        printf '%s' "$missing" >&2
    fi
fi

[[ $stale -eq 0 ]] || exit 1

if [[ $check_only -eq 1 ]]; then
    echo "apply-presets: up to date ($rows settings)."
else
    generated=$(grep -c 'public const' "$defaults_out")
    presets=$(grep -c 'PresetId preset) =>' "$values_out")
    echo "apply-presets: wrote $generated defaults and $presets preset rows from $rows settings."
fi
