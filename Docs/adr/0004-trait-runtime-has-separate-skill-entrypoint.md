# Trait runtime has a separate skill entrypoint

Trait tier skills are executed through a dedicated runtime entrypoint rather than being appended to each unit's native skill list. `SynergyManager` owns trait counting and active tier selection, `SkillRuntimeManager` owns skill execution, and the UI consumes a prepared trait display model; this keeps trait activation from being confused with per-unit native skills.

Implemented shape: `SynergyManager.ActiveTraitTiers` exposes active tier SkillIDs and qualifying deployed player units. At battle start, `SkillRuntimeManager` builds trait-tier runtimes from those models, resolves battle-start trait skills before native unit battle-start skills, and ticks periodic trait skills during battle. Global target types resolve once per active tier and evaluate formulas against each affected target; local one-to-many target types resolve once per qualifying trait unit and evaluate formulas against that unit as the source.
