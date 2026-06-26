# Traits grant skills instead of implementing effects

Status: superseded in part by ADR-0002

Active trait tiers grant Skill IDs rather than implementing their own shield, damage, revive, summon, or stat-effect logic. Trait activation counts only distinct deployed player units, granted skills use their own targeting rule, each receiving unit is the skill bearer for value calculations, granted skills remain fixed for the battle, and duplicate Skill IDs are de-duplicated; this keeps traits responsible for lineup activation while the skill table remains the single place for battle effects.
