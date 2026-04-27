# UI Design Rules

Collected from design docs and past session mistakes. Check these BEFORE writing any CSS or UI code.

## Font Sizes (STRICT)

**Only 12px or 14px.** No 13px, 15px, or other values. This is in AI_GUIDE.md and is non-negotiable.

- 14px — Standard body text, buttons, inputs
- 12px — Secondary text, labels, metadata, badges

## Colors & Theme System

**CSS variables only** — `var(--color-*)`. Never hardcode hex colors except in theme definitions.

The theme system is centralized in [src/styles/theme-colors.css](../../BrickBot.Client/src/styles/theme-colors.css):
- Both light + dark palettes live in one file, switched via `[data-theme="light|dark"]` on `<html>`.
- The attribute is set by `App.tsx` (driven by `useSettingsStore.resolvedTheme`) AND by `settingsOperations.applyResolvedThemeToBody()` after backend events. Both code paths must use `document.documentElement` (NOT `document.body`).
- AntD's `theme.{darkAlgorithm|defaultAlgorithm}` is set via `<ConfigProvider>` from the same `resolvedTheme` value — always keep the two in sync.
- Layout heights are CSS vars too: `--layout-header-height`, `--layout-statusbar-height`. Reference these in component CSS instead of hardcoding pixels.

**Available CSS variables** (use these, not raw hex):
- Backgrounds: `--color-bg-{base,container,elevated,layout,spotlight,mask}`
- Borders: `--color-border-{base,secondary}`
- Text: `--color-text-{base,secondary,tertiary,quaternary,inverse}`
- Primary: `--color-primary`, `--color-primary-{hover,active,bg,bg-hover}`
- Status: `--color-{success,warning,error,info}`, `--color-{success,warning,error,info}-bg`
- Components: `--color-{card-bg,card-header-bg,input-bg,input-border,table-header-bg,table-row-hover,header-bg,header-text}`
- Shadows: `--shadow-{base,elevated,card}`
- Radii: `--border-radius-{base,sm,lg}` (sharper than AntD defaults)

**Resolved theme & system preference**: when the user picks "auto", `settingsStore.resolveTheme()` falls back to `window.matchMedia('(prefers-color-scheme: dark)')`. The picker writes `'auto' | 'light' | 'dark'` to backend; what the UI uses is `resolvedTheme: 'light' | 'dark'`.

## Ant Design Component Gotchas

### `danger` prop causes icon button misalignment

Ant Design's `danger` prop on `<Button>` uses a different internal rendering path than `type="primary"`. When placed side-by-side, icon-only `danger` buttons render at a slightly different vertical position than `primary` buttons.

**Workaround:** Use inline `style={{ color: 'var(--color-error)' }}` on the icon instead of the `danger` prop when alignment with adjacent buttons matters. Or accept the minor visual difference for non-icon buttons where it's less noticeable.

### `Empty` component is for "no data" states only

Don't use `<Empty>` for hero/landing screens. It adds unwanted default styling and semantics. Build custom hero layouts with plain divs + BEM classes.

## Window Size & Position Persistence

The main window's size, position, and maximized state persist across launches via the **backend** — frontend code never touches `window.resizeTo` / `window.moveTo`.

**Lifecycle**:
1. **Load on bootup** — `ApplicationHost.CreateMainForm` calls `IWindowStateService.LoadWindowStateAsync()` BEFORE the form is shown. State is read from `data/settings/global.json` (`Window` section). If the saved (x, y) isn't visible on any current monitor, the form auto-centers on the primary screen instead.
2. **Save on close** — `ApplicationHost.OnFormClosed` calls `SaveWindowStateAsync(form)`. Maximized state is saved separately; if maximized, position/size are NOT overwritten so the next normal-state restore uses the prior values.
3. **Live reset** — Settings UI's "Reset Window State" button hits `SETTING.RESET_WINDOW_STATE`, which clears `Window.{X,Y,Width,Height}` and emits `SettingEvents.WINDOW_STATE_RESET`. `ApplicationHost.HandleWindowStateResetAsync` listens and re-centers/re-sizes the form on the UI thread.

**DPI handling**: `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` is set in `ApplicationBootstrapper.InitializeWinForms`. All saved coordinates are in **logical pixels** — Windows scales them per-monitor automatically. Never manually multiply by `DpiX/96`.

**Add a new persistent UI dimension**: add the field to `Modules/Setting/Models/GlobalSettings.cs` (or to a profile-scoped `ProfileConfiguration` if it varies per profile), then wire SettingFacade reads/writes. Don't invent a separate JSON file — `global.json` is the single global store.

## Compact Component Library (USE THESE, not raw AntD)

The shared **compact** library wraps AntD with consistent heights, tighter padding,
and theme-aware disabled/hover states — mirroring BrickBot. Always prefer
these over raw AntD for any new code, and migrate existing usages when you touch them.

```ts
import {
  CompactCard, CompactSection, CompactDivider, CompactSpace,
  CompactButton, CompactPrimaryButton, CompactDangerButton, CompactWarningButton, CompactSuccessButton, CompactTextButton,
  CompactSelect, CompactInput, CompactTextArea, CompactPassword,
  CompactSwitch, CompactAlert,
  CompactTitle, CompactParagraph, CompactText,
} from '@/shared/components/compact';
```

| Raw AntD | Use instead |
|---|---|
| `<Card>` | `<CompactCard>` (pass `extraCompact` for tighter padding) |
| `<Button>` | `<CompactButton>` — `size: small\|medium\|large` (default `medium` = 32px) |
| `<Button type="primary">` | `<CompactPrimaryButton>` (also `<CompactButton.Primary>`) |
| `<Button danger>` | `<CompactDangerButton>` |
| `<Select>` | `<CompactSelect>` |
| `<Input>` / `<Input.TextArea>` / `<Input.Password>` | `<CompactInput>` / `<CompactTextArea>` / `<CompactPassword>` |
| `<Switch>` | `<CompactSwitch>` (rectangular, theme-aware) |
| `<Alert>` | `<CompactAlert>` (pass `extraCompact` for very tight spaces) |
| `<Modal>` for forms | `<FormDialog>` from `@/shared/components/dialogs` |
| One-off "are you sure" | `<ConfirmDialog>` from `@/shared/components/dialogs` (`okType="danger"` for destructive) |
| `<Drawer>` (right side panel) | `<SlideInScreen>` from `@/shared/components/common` (blurred-backdrop slide-in, ESC-to-close) |
| `<Space>` | `<CompactSpace>` (size='small' default) |
| `<Typography.Title>` / `<Typography.Paragraph>` | `<CompactTitle>` / `<CompactParagraph>` (zero-margin defaults) |

**Padding control on `<CompactCard>`** — three escape hatches in precedence order:
- `bodyStyle={{ padding: 0 }}` wins all (use for Monaco-embed cards / canvas surfaces)
- `padding={N}` shorthand for one-off body padding tweaks
- `extraCompact` → 8px body padding (use in dense toolbars/lists)
- default → 12px body padding (was 16px — softened so cards don't dwarf their content)

**Padding control on `<SlideInScreen>`** — pass a `bodyClassName` and write a class with `padding: 0 !important` (e.g. `.scripts-view-capture-body`) when the embedded panel manages its own padding.

**Variants kept on raw AntD** (no compact wrapper exists yet — add one only when you find yourself repeating customization):
- `<Tag>`, `<Tooltip>`, `<Popconfirm>`, `<List>`, `<Form>`, `<Form.Item>`, `<Row>` / `<Col>`, `<Tabs>`, `<Drawer>`, `<Dropdown>`, `<ColorPicker>`, `<Spin>`, `<Empty>`, `<Flex>`. Use `<CompactSpace>` for `<Space>` (small-default).

**Common widgets** (`@/shared/components/common`): `<CountBadge>`, `<StatusIcon>`, `<CloseButton>`.

**Adding a new compact wrapper**: copy the shape of e.g. `CompactSelect.tsx` — `Omit<...Props, 'size'>`, default to `'medium'`, append `compact-X compact-X-{size}` classes, write the matching `.css` with the three height variants (24/32/40). Then export from `compact/index.ts`.

## Pattern Reuse (CHECK FIRST)

Before building a new UI pattern, **search for existing implementations**:

| Need | Search for |
|---|---|
| Confirmation dialog | `ConfirmDialog` in `shared/components/dialogs/` |
| Form-shaped dialog | `FormDialog` in `shared/components/dialogs/` |
| Compact buttons/inputs | `shared/components/compact/` |
| Slide-in side panel | `SlideInScreen` in `shared/components/common/` |
| Count badges | `CountBadge` in `shared/components/common/` |
| Status indicator (loaded/not) | `StatusIcon` in `shared/components/common/` |
| Square close button | `CloseButton` in `shared/components/common/` |

**Never build a new TreeSelect for categories** — use the shared `CategorySelect` component (flat dropdown with breadcrumb labels).
