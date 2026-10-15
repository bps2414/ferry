# Nova marca e prints do README

Três entregas: logo com conceito novo, GIF da logo e captura da interface atual nos READMEs.

## Decisões

- **Logo:** conceito novo. O F de "fita dobrada" sai.
- **GIF:** entrada curta (~1,5 s) e depois para. Sem loop chamativo.
- **Prints:** a interface atual (redesenho já entregue), gerada pelo `e2e/web/showcase.mjs`.

## Estado atual

- `tools/generate-brand.py` é a fonte única da marca. Ele gera os SVGs de `web/ui/brand/`, o favicon `web/ui/logo.svg`, `app/BrandGeometry.xaml` (logo animada no Windows), `app/Ferry.ico`, os trechos da marca em `web/ui/index.html`, `docs/brand/preview.png` e os GIFs (`docs/brand/ferry-logo-animated.gif` e `docs/logo_animated.gif`).
- Os três READMEs usam `docs/brand/ferry-logo-animated.gif` (340 px) e `docs/screenshots/fila.png`. Esse print é da interface antiga: barra lateral sem logo, sem os grupos "Precisa de você", "Enviando" e "Na fila".
- `docs/screenshots/` também tem `configuracoes.png`, `log.png` e `vazio.png`, todos antigos. Nenhum README usa esses arquivos.

## Fase 1: conceito da logo

1. Desenhar 3 propostas de símbolo em SVG, cada uma com o nome ao lado. As ideias de partida são:
   - **Travessia:** casco de balsa abstrato ou uma onda que forma o movimento de levar algo de um lado para o outro.
   - **Envio:** seta ou trilho entrando numa forma que lembra o console, sem copiar a marca da Sony.
   - **Monograma:** "F" geométrico novo, de traço único, sem gradientes nem peças sobrepostas.
2. Montar uma prancha para comparar as propostas: fundo escuro e claro, 16, 32 e 128 px, e o nome ao lado. Você escolhe uma; se precisar, faço uma rodada de ajustes.
3. Regras para todas as propostas: no máximo 2 cores e só cor chapada, sem gradiente "perolado". O símbolo precisa ser legível em 16 px. O nome continua em Geist SemiBold convertida em contornos (a fonte já vem com o app, licença OFL).

## Fase 2: implementar a marca escolhida

1. Trocar em `tools/generate-brand.py` a geometria (`SHAPES`, `FILLS`, `TRACE`), as cores e o badge pela proposta escolhida. Manter os mesmos arquivos de saída para não mexer em quem consome a marca.
2. Refazer a animação: entrada de ~1,5 s com no máximo 2 movimentos, que termina parada. Os mesmos keyframes servem para CSS, WPF e GIF, como hoje.
3. GIF do README: fundo transparente ou casado com o tema escuro do GitHub. Precisa ter borda limpa, sem halo de paleta. A pausa longa entre repetições deixa o GIF quase sempre parado.
4. Rodar `python tools/generate-brand.py` e depois `--check`. Atualizar `docs/brand/README.md` e `preview.html` para o novo conceito.
5. Conferir a marca no app Windows (`BrandLogo` e ícone da bandeja/exe) e na web (login, barra lateral e favicon), incluindo movimento reduzido.

## Fase 3: prints do README

1. `dotnet build web -c Release` e depois `node e2e/web/showcase.mjs`, já com a marca nova.
2. Copiar para `docs/screenshots/` só as capturas usadas:
   - `fila.png` sai de `en-fila-desktop.png` e vai para o `README.md` e o `README.es.md` (a interface não tem espanhol).
   - `fila.pt-BR.png` sai de `pt-BR-fila-desktop.png` e vai para o `README.pt-BR.md`.
3. Apagar `configuracoes.png`, `log.png` e `vazio.png` antigos, que nenhum README usa.
4. Opcional: adicionar ao README uma captura do celular (`*-fila-celular.png`) ao lado da do desktop, mostrando que a fila também funciona no telefone.

## Critério de pronto

- No GitHub, o README abre com a logo nova animando uma vez e parando, e o print mostra a interface atual no idioma do README.
- App Windows, web e `.ico` usam a mesma marca nova. `generate-brand.py --check` passa.
- Os arquivos da marca antiga somem do repositório (`docs/logo.png`, `docs/logo_animated.gif` e o preview antigo, que são regenerados) e nada aponta para eles.
