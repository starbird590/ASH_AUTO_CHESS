# Trait tier skills resolve once

Status: superseded in part by ADR-0005

Active trait tiers schedule their Skill IDs once per battle tier instead of copying those skills onto every deployed unit that has the trait. This prevents a team-wide buff from scaling accidentally with the number of qualifying units while keeping the skill table responsible for the actual targeting and effect behavior.

Trait tier skills do not pick a representative unit for team-wide calculation. Team-wide percentage effects calculate against each skill target's own attributes, while local one-to-many effects use the skill source's position and source-side attributes.

When a trait tier skill uses a local one-to-many target rule, each qualifying deployed unit with that trait may act as a source for that local effect. This is distinct from accidental duplicate casting: global or team-wide trait tier skills still resolve only once for the active tier.

Only the highest active tier of a trait contributes trait tier skills. Higher tiers replace lower tiers, so a higher-tier row must list every Skill ID that should be active at that tier.

Trait tier descriptions are UI text only. The runtime does not parse descriptions to determine battle behavior; Skill IDs remain the source of truth for effects.
