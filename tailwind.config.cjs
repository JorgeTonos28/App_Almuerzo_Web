module.exports = {
  content: ['./src/Almuerzo.Web/Templates/*.html', './src/Almuerzo.Web/wwwroot/assets/app.js', './src/Almuerzo.Web/Views/**/*.cshtml'],
  safelist: [{ pattern: /(bg|text|border|from|to|ring)-(blue|indigo|amber|gray|red|green|purple|orange|yellow|emerald|rose|slate)-(50|100|200|300|400|500|600|700|800|900)/ }],
  theme: { extend: {} }, plugins: []
};
