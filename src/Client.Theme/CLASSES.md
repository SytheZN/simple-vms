# Theme Class Reference

All styling uses classes from `src/theme.css`. Tailwind utilities may be used
for layout (flex, grid, spacing, sizing) but visual appearance must come from
these classes. Icons use Phosphor font: `<i class="ph ph-{name} icon-{size}">`.

## Buttons

Use `btn` base + variant. Add size modifier if needed.

| Class | Usage |
|-------|-------|
| `btn` | Base - always required |
| `btn-primary` | Primary action |
| `btn-secondary` | Secondary action (bordered) |
| `btn-danger` | Destructive action |
| `btn-ghost` | Minimal/transparent |
| `btn-sm` | Small size modifier |
| `btn-lg` | Large size modifier |

```html
<button class="btn btn-primary"><i class="ph ph-plus icon-sm"></i> Add</button>
<button class="btn btn-danger btn-sm" disabled>Delete</button>
```

## Badges

Use `badge` base + variant.

| Class | Usage |
|-------|-------|
| `badge` | Base - always required |
| `badge-success` | Positive status (online, recording) |
| `badge-warning` | Attention needed |
| `badge-danger` | Error/critical |
| `badge-neutral` | Inactive/default (offline, idle) |

```html
<span class="badge badge-success"><i class="ph-fill ph-circle icon-sm"></i> Online</span>
```

## Form

| Class | Usage |
|-------|-------|
| `input` | Text input, select, textarea |
| `input-error` | Add to `input` for error state |
| `label` | Form field label |
| `toggle-track` | Toggle switch container (use `role="switch"` + `aria-checked`) |
| `toggle-knob` | Toggle switch knob (child of track) |

```html
<label class="label">Name</label>
<input class="input" />
<input class="input input-error" />
<button class="toggle-track" role="switch" aria-checked="true">
  <span class="toggle-knob"></span>
</button>
```

## Card

| Class | Usage |
|-------|-------|
| `card` | Container with raised surface, border, shadow |

```html
<div class="card p-4">Content</div>
<div class="card overflow-hidden"><!-- image + content --></div>
```

## Toast

Use `toast` base + variant.

| Class | Usage |
|-------|-------|
| `toast` | Base - always required |
| `toast-success` | Success message |
| `toast-danger` | Error message |
| `toast-warning` | Warning message |
| `toast-info` | Neutral/informational |

```html
<div class="toast toast-success">
  <i class="ph ph-check-circle icon-xl"></i>
  <div><span class="font-medium">Title</span><p>Message</p></div>
</div>
```

## Navigation

| Class | Usage |
|-------|-------|
| `nav-sidebar` | Sidebar container |
| `nav-link` | Sidebar link |
| `nav-link-active` | Add to active `nav-link` |
| `nav-link-toggle` | Caret icon in expandable link (auto right-aligned) |
| `nav-link-toggle-open` | Add when expanded (flips caret) |
| `nav-children` | Container for child links |
| `nav-child` | Child navigation link |
| `nav-child-active` | Add to active `nav-child` |

```html
<nav class="nav-sidebar">
  <a class="nav-link nav-link-active"><i class="ph ph-squares-four icon-sm"></i> Gallery</a>
  <a class="nav-link">
    <i class="ph ph-gear icon-sm"></i> Settings
    <i class="ph ph-caret-down icon-sm nav-link-toggle nav-link-toggle-open"></i>
  </a>
  <div class="nav-children">
    <a class="nav-child nav-child-active"><i class="ph ph-faders icon-sm"></i> General</a>
    <a class="nav-child"><i class="ph ph-hard-drives icon-sm"></i> Storage</a>
  </div>
</nav>
```

## Table

| Class | Usage |
|-------|-------|
| `table` | Table element. Wrap in `card overflow-hidden` |

```html
<div class="card overflow-hidden">
  <table class="table">
    <thead><tr><th>Col</th></tr></thead>
    <tbody><tr><td>Val</td></tr></tbody>
  </table>
</div>
```

## Headings

| Class | Usage |
|-------|-------|
| `section-heading` | Page/section title with bottom border |
| `section-subheading` | Uppercase label for subsections or stat cards |

## Modal

| Class | Usage |
|-------|-------|
| `modal-container` | Fixed fullscreen flex container |
| `modal-backdrop` | Dark overlay (child of container, must come before content) |

Content uses `card` with `relative` to sit above backdrop.

```html
<div class="modal-container">
  <div class="modal-backdrop"></div>
  <div class="relative card p-6 max-w-md shadow-modal">Content</div>
</div>
```

## Progress

| Class | Usage |
|-------|-------|
| `progress-track` | Bar background |
| `progress-fill` | Fill bar (set width via style). Default: primary color |
| `progress-fill-success` | Success color variant |
| `progress-fill-warning` | Warning color variant |
| `progress-fill-danger` | Danger color variant |

```html
<div class="progress-track">
  <div class="progress-fill" style="width: 68%;"></div>
</div>
```

## Spinner

| Class | Usage |
|-------|-------|
| `spinner` | Default size (icon-lg) |
| `spinner-sm` | Small (icon-sm) |
| `spinner-lg` | Large (icon-xl) |

```html
<div class="spinner"></div>
<button class="btn btn-primary" disabled><div class="spinner spinner-sm"></div> Loading</button>
```

## Timeline

| Class | Usage |
|-------|-------|
| `timeline-bar` | Track background |
| `timeline-span` | Base for duration spans (set left/width via style) |
| `timeline-span-recording` | Recording span color |
| `timeline-span-motion` | Motion span color |
| `timeline-marker` | Base for point markers (set left via style) |
| `timeline-playhead` | Playhead marker |
| `timeline-alert` | Alert marker |
| `timeline-tick` | Time label below bar (set left via style). Tick line is `::before` |

```html
<div class="timeline-bar">
  <div class="timeline-span timeline-span-recording" style="left: 0%; width: 35%;"></div>
  <div class="timeline-marker timeline-playhead" style="left: 85%;"></div>
</div>
<div class="relative h-4">
  <div class="timeline-tick" style="left: 0%;">12:00</div>
</div>
```

## Video Overlay

| Class | Usage |
|-------|-------|
| `video-overlay-text` | White text with heavy shadow for video overlays. Always white in both themes. |

## Icons

Phosphor icon font. Use `<i class="ph ph-{name} icon-{size}">`.

| Class | Size |
|-------|------|
| `icon-sm` | 18px |
| `icon-md` | 22px |
| `icon-lg` | 26px |
| `icon-xl` | 32px |

Filled variants: `ph-fill ph-{name}`.
