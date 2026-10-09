import fs from 'node:fs/promises';
const out = 'src/Almuerzo.Web/wwwroot/assets';
await fs.mkdir(`${out}/webfonts`, { recursive: true });
await fs.writeFile(`${out}/app.css`, (await fs.readFile('frontend/app.css', 'utf8')) + '\n' + (await fs.readFile('frontend/responsive.css', 'utf8')));
await fs.copyFile('node_modules/vue/dist/vue.runtime.global.prod.js', `${out}/vue.js`);
await fs.copyFile('node_modules/@fortawesome/fontawesome-free/css/all.min.css', `${out}/icons.css`);
// Keep paths expected by Font Awesome's CSS.
await fs.cp('node_modules/@fortawesome/fontawesome-free/webfonts', 'src/Almuerzo.Web/wwwroot/webfonts', { recursive: true });
const fonts = [];
for (const weight of [300, 400, 500, 600, 700]) {
  const name = `inter-latin-${weight}-normal.woff2`;
  await fs.copyFile(`node_modules/@fontsource/inter/files/${name}`, `${out}/${name}`);
  fonts.push(`@font-face{font-family:Inter;font-style:normal;font-weight:${weight};font-display:swap;src:url('/assets/${name}') format('woff2')}`);
}
await fs.writeFile(`${out}/fonts.css`, fonts.join('\n'));
await fs.mkdir('src/Almuerzo.Web/wwwroot/licenses', { recursive: true });
for (const [source, name] of [['vue/LICENSE', 'vue.txt'], ['tailwindcss/LICENSE', 'tailwind.txt'], ['@fontsource/inter/LICENSE', 'inter.txt'], ['@fortawesome/fontawesome-free/LICENSE.txt', 'fontawesome.txt']])
  await fs.copyFile(`node_modules/${source}`, `src/Almuerzo.Web/wwwroot/licenses/${name}`);
