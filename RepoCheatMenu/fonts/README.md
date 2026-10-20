# fonts/

Эта папка нужна для сборки, но её содержимое НЕ в git — шрифты
`SFProDisplay-Regular.otf` и `SFProDisplay-Medium.otf` принадлежат
Apple и их лицензия не разрешает включать их в репозиторий.

**Как положить шрифты сюда вручную:**

1. Скачай SF Pro с сайта Apple:
   https://developer.apple.com/fonts/ (нужна учётка Apple ID,
   пакет называется «San Francisco Pro»).
2. Установи `.dmg`/`.pkg` или просто распакуй.
3. Скопируй сюда ровно эти два файла:
   - `SFProDisplay-Regular.otf`
   - `SFProDisplay-Medium.otf`

Без них мод соберётся, но шрифт меню / HUD будет fallback
(Segoe UI / Arial).

Шрифты попадают в собранный пакет (`dist/RepoLauncher/packages/
RepoCheatMenu/fonts/`), но не распространяются через git.
