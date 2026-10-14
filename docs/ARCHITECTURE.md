# Architecture

## Initial application
- WPF UI, moving toward MVVM.
- Keep the UI thread for presentation and short state changes.
- File enumeration should move to background tasks as the library grows.
- Local files are referenced by path; media is not copied by default.

## Planned boundaries
- Core: models, commands, app settings.
- Library: SQLite catalog, import and search.
- Audio: music playback, effect slots, mixing and output device selection.
- Video: playback and media source lifecycle.
- Output: monitor detection and fullscreen Program window.
- Scenes: source references, graphics and transitions.
- Presentations: PowerPoint automation and contest templates.

## Performance policy
- Avoid decoding every media file during import.
- Load thumbnails on demand.
- Keep Preview decoding optional and bounded.
- Test managed and native memory on an 8 GB Windows machine.
- Keep audio playback independent from video failures.
