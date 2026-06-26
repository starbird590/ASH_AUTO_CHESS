# Trait tables define tiered skill activation

Trait data is split into a base trait table for identity and a child trait table for activation tiers. Excel remains the design source, while Unity imports CSV exports into `TraitSO` assets; the child table's highest active tier supplies the full set of Skill IDs for that trait, while tier descriptions remain UI text only. This keeps table editing clear, keeps Unity assets inspectable, and leaves battle behavior in the skill table.
