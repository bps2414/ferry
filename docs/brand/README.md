# Ferry — travessia

A marca é uma balsa geométrica (cabine e casco) sobre uma onda azul: levar algo
de um lado para o outro. Só cores chapadas: o texto da interface (`#E9EDF3`, ou
`#0D1117` em fundo claro) e o azul de destaque (`#6F97FF`, ou `#3D6BE0` no claro).
O nome usa contornos da Geist SemiBold já distribuída no aplicativo, sob a licença
OFL em `app/fonts/OFL.txt`.

`tools/generate-brand.py` é a fonte da geometria, do espaçamento do nome e dos
tempos da animação. Gera os SVGs em `web/ui/brand/`, o favicon `web/ui/logo.svg`,
os contornos e o storyboard nativo em `app/BrandGeometry.xaml`, os trechos da
marca em `web/ui/index.html` e as exportações PNG, ICO e GIF.

## Reprodução

O aplicativo não ganha dependências. As ferramentas abaixo só são usadas para
gerar os assets; os arquivos gerados ficam no repositório.

```powershell
python -m pip install -r tools/brand-requirements.txt
python tools/generate-brand.py
python tools/generate-brand.py --check
```

`--vectors-only` dispensa Pillow/resvg e atualiza apenas SVG, CSS, XAML e os
trechos HTML. O exportador também reconhece dependências instaladas na pasta
ignorada `dist-brand/python`.

Para ver a marca, os tamanhos pequenos e repetir a animação:

```powershell
python -m http.server 8032 --bind 127.0.0.1
```

Abra `http://127.0.0.1:8032/docs/brand/preview.html`. `preview.png` é a prancha
estática. Os READMEs usam `ferry-logo-animated.gif` (tema escuro do GitHub) e
`ferry-logo-animated-light.gif` (tema claro), escolhidos por `<picture>`.

## Movimento e integração

- Formação em 1,5 s: a onda se desenha, o casco e a cabine sobem até ela e o
  nome aparece. A marca permanece estática ao terminar.
- Web: SVG inline e CSS, uma abertura por carregamento da página, no primeiro
  espaço visível (login ou barra lateral).
- SVG animado independente: mesmo movimento, sem scripts nem recursos externos.
- Windows: `BrandLogo` usa os mesmos paths, cores e tempos, com animação WPF.
  Ao descarregar o controle, remove os clocks e o listener de preferências.
- Movimento reduzido: `prefers-reduced-motion` na web e
  `SystemParameters.ClientAreaAnimation` no Windows. A marca estática fica visível.
- GIF: 50 quadros/s com uma única paleta. Toca uma vez e para na marca pronta.
- ICO: quadros individuais de 16, 24, 32, 48, 64, 128 e 256 px, renderizados
  separadamente para cada tamanho.
- Ícone de janela/executável/bandeja: continuam consumindo `app/Ferry.ico`.

Os SVGs têm `viewBox`, título acessível e contornos vetoriais. O nome não depende
de fontes carregadas em tempo de execução. As versões em fundo claro e em uma
cor estão em `ferry-logo-light.svg` e `ferry-logo-mono.svg`.
