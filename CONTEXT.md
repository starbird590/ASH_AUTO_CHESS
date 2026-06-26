# ASH Auto Chess

ASH Auto Chess is a 2D auto-chess game about choosing stages, preparing a squad, deploying units, and resolving automatic battles. This glossary keeps gameplay terms precise so data tables, Unity assets, and code describe the same concepts.

## Language

**Unit**:
A combat piece that can be owned by the player, the enemy, or neither side during battle.
_Avoid_: Character, chess object, prefab

**Deployed Unit**:
A unit that starts or participates on the battlefield as part of the current battle lineup.
_Avoid_: Board unit, active prefab

**Summoned Unit**:
A temporary unit created by an in-battle effect. Summoned units can fight, but they are not counted as lineup members for trait activation.
_Avoid_: Spawn, clone, extra unit

**Trait**:
A squad tag that can activate when enough distinct deployed player units share it.
_Avoid_: Synergy, union, faction

**Trait Tier**:
The active strength level of a trait after its unit count reaches a configured threshold.
_Avoid_: Trait level, breakpoint

**Trait Tier Description**:
Player-facing text that explains what a trait tier appears to do. Battle behavior is defined by the tier's skills, not by parsing this description.
_Avoid_: Effect script, parsed description

**Trait Tier Skill**:
A skill made available by one active trait tier during a battle. Team-wide trait tier skills resolve for the tier, while local or event-bound trait tier skills may use qualifying deployed units as skill sources.
_Avoid_: Per-unit trait skill, repeated synergy cast

**Team-Wide Trait Tier Skill**:
A trait tier skill whose target group affects the whole eligible side or battle. It resolves once per active trait tier, and target-side percentage effects use each target's own attributes.
_Avoid_: Global per-unit cast

**Local Trait Tier Skill**:
A trait tier skill whose target group is centered on a qualifying unit. Each qualifying deployed unit may act as a skill source because the effect depends on that unit's position or source-side attributes.
_Avoid_: Accidental duplicate buff

**Event-Bound Trait Tier Skill**:
A trait tier skill triggered by a qualifying deployed unit's battle event, such as taking damage, killing an enemy, or making a counted attack. The event unit is the skill source for that trigger.
_Avoid_: Global event skill, copied native skill

**Trait Count**:
The number of distinct deployed player units that qualify a trait for activation. Summoned units do not contribute to trait count.
_Avoid_: Synergy count, tag count

**Trait Identity Key**:
The stable identity used to count whether deployed units are the same unit for trait activation. Different star tiers of the same unit share one trait identity key and therefore count once.
_Avoid_: Star-specific unit ID, object instance

**Trait Summary**:
The compact in-battle presentation of a trait's name, current count, and tier thresholds.
_Avoid_: Full trait details

**Trait Details**:
The expanded player-facing view of a trait's description and all tier descriptions.
_Avoid_: Parsed effect source

**Skill**:
A data-defined battle effect with a trigger, target group, effect type, and effect parameters.
_Avoid_: Ability, spell, passive

**Skill Bearer**:
The unit that owns or receives a native skill. Trait tier skills do not require a bearer for team-wide calculation.
_Avoid_: Caster

**Skill Source**:
The unit a skill is launched from when the effect needs a position, radius, attack event, or source-side attribute. A trait tier skill only has a skill source when its targeting rule is local to a unit.
_Avoid_: Representative unit

**Skill Target**:
A unit selected by a skill's target group and affected by the resolving effect. Target-side percentage effects use each target's own attributes.
_Avoid_: Affected prefab

**Skill Target Group**:
The group of units selected by a skill's targeting rule when the skill resolves.
_Avoid_: Skill range, affected scope

**Native Skill**:
A skill that belongs to a unit by its own unit data.
_Avoid_: Base ability, built-in skill

**Trait-Granted Skill**:
A skill made available by an active trait tier for the duration of a battle. For trait tiers, the skill resolves once per active tier rather than once per unit that has the trait.
_Avoid_: Trait effect, synergy buff skill

**Mechanism**:
A special skill behavior selected inside a mechanism-class skill effect.
_Avoid_: Custom effect, special case
