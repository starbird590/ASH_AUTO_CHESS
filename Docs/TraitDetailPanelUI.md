# Trait Detail Panel UI

The trait detail panel is a hold-to-view popup for the compact trait list.

Runtime behavior:

- `SynergyItemUI` detects a long press on one trait row.
- `SynergyListUI` passes the selected `TraitSynergyDisplayModel` to the detail view.
- The popup text includes the trait `UnionDes` and every tier `LevelDes`.
- The popup uses a fixed width chosen in Unity and an adaptive height driven by Unity layout components.
- Code does not own visual styling such as font size, text color, panel size, padding, background, or scroll behavior.
- Code positions the popup by moving the panel pivot to the long-press pointer position. With pivot `(0, 1)`, this means the panel's top-left corner matches the pointer point.
- The trait list lives on the left side of the screen, so the popup does not need generic screen-edge avoidance logic.
- The unit icon list shows every player unit in the collection that owns the trait.
- Unit icons are highlighted only when that unit identity is currently deployed and counted for the trait.
- Same-unit different-star entries share one unit identity, so only one icon identity is highlighted.
- Same-unit different-star entries are shown as one collection icon, using the 1-star unit icon as the preferred representative.
- Unit icon visuals are owned by a Unity prefab. Code only sets the sprite and toggles active/highlight state objects.
- `SynergyDetailPanelUI` exposes only content bindings in the Inspector. Position helpers such as the panel RectTransform and Canvas are resolved by the script.
- `unitIconRoot` is part of the panel's vertical layout and should be the last layout child, so the unit icon list always stays at the bottom of the adaptive panel.

Recommended Unity setup:

1. Create a `SynergyDetailPanel` GameObject under the same Canvas as the trait list.
2. Set the panel RectTransform pivot to top-left `(0, 1)` so the script can align that corner to the long-press point.
3. Use Unity layout components to control fixed width, adaptive height, padding, text size, colors, and optional scrolling.
4. Add child legacy `Text` objects for title and body.
5. Add a unit icon container for the trait collection icons.
6. Create a unit icon item prefab with `SynergyDetailUnitIconUI`.
7. Bind the icon Image, optional name Text, active highlight object, and inactive overlay object on the icon item prefab.
8. Bind title, body, unit icon root, and unit icon prefab to `SynergyDetailPanelUI`.
9. Bind the panel component to `SynergyListUI.detailPanelView`.
10. Leave the panel inactive by default; the script will show and hide it during long press.

Suggested hierarchy:

```text
SynergyDetailPanel
  TitleText
  BodyText
  UnitIconRoot
```

Suggested component setup:

- `SynergyDetailPanel`
  - `RectTransform`
    - Pivot: `X = 0`, `Y = 1`.
    - Anchors: left/top is recommended for editor readability, but runtime positioning uses the panel pivot instead of `anchoredPosition`.
    - Width: fixed by hand, for example `300`.
    - Height: leave it to layout; do not maintain it by script.
  - `Image`
    - Optional panel background.
  - `Vertical Layout Group`
    - Padding: choose visually in Unity, for example left/right/top/bottom `8`.
    - Spacing: choose visually in Unity, for example `4` or `6`.
    - Child Alignment: `Upper Left`.
    - Control Child Size - Width: on.
    - Control Child Size - Height: on.
    - Use Child Scale - Width: off.
    - Use Child Scale - Height: off.
    - Child Force Expand - Width: on.
    - Child Force Expand - Height: off.
  - `Content Size Fitter`
    - Horizontal Fit: `Unconstrained`.
    - Vertical Fit: `Preferred Size`.
  - `SynergyDetailPanelUI`: bind `TitleText`, `BodyText`, `UnitIconRoot`, and the unit icon prefab.
- `TitleText`
  - `Text`: the trait name.
  - `Layout Element`: optional. Use it only if Unity needs help calculating text height.
- `BodyText`
  - `Text`: the trait description and all tier descriptions.
  - `Horizontal Overflow`: `Wrap`.
  - `Vertical Overflow`: `Overflow` or `Truncate`, depending on whether the panel may grow freely.
  - `Layout Element`: optional. Use it only if Unity needs help calculating text height.
- `UnitIconRoot`
  - `Horizontal Layout Group` or `Grid Layout Group`: lays out the collection icons.
  - Keep it as the last child under `SynergyDetailPanel`; do not position it separately by script.
  - If using `Horizontal Layout Group`:
    - Child Alignment: `Middle Left`.
    - Control Child Size - Width: on.
    - Control Child Size - Height: on.
    - Child Force Expand - Width: off.
    - Child Force Expand - Height: off.
  - If using `Grid Layout Group`:
    - Cell Size: set to the icon prefab size, for example `32 x 32`.
    - Spacing: choose visually in Unity, for example `4 x 4`.
    - Start Corner: `Upper Left`.
    - Start Axis: `Horizontal`.
- Unit icon item prefab
  - `SynergyDetailUnitIconUI`: bind the icon Image, optional name Text, active highlight object, and inactive overlay object.

Only keep a separate `VerticalContent` object if you need an extra wrapper for masking or scrolling. In that case, put the `Vertical Layout Group` and `Content Size Fitter` on `VerticalContent`, keep `TitleText`, `BodyText`, and `UnitIconRoot` as its children, and keep `UnitIconRoot` as the last child.
