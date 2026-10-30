<p align="center">
  <img src="assets/banner.png" alt="JellyIPTV" width="640">
</p>

<p align="center">
  Plugin para <b>Jellyfin 10.10</b> que traz filmes, séries e TV ao vivo de uma conta <b>Xtream Codes</b> para dentro do Jellyfin, e deixa você baixar o que quiser para a sua biblioteca.
</p>

<p align="center">
  <a href="https://github.com/KsmKayk/jellyiptv/releases/latest"><img src="https://img.shields.io/github/v/release/KsmKayk/jellyiptv?label=release&color=8b3fd9" alt="Release"></a>
  <img src="https://img.shields.io/badge/Jellyfin-10.10.x-a855f7" alt="Jellyfin 10.10">
</p>

---

## O que ele faz

- **Canal IPTV** com *Filmes* e *Séries*: navegue por categoria → série → temporada → episódio. As pastas *Todos os filmes* e *Todas as séries* mostram o catálogo inteiro em ordem alfabética.
- **TV ao vivo** integrada à seção nativa *TV ao vivo* do Jellyfin, com **guia de programação** (EPG).
- **Botão de download** no navegador e no app desktop: *Baixar filme*, *Baixar série inteira*, *Baixar temporada* ou *Baixar episódio*. Os arquivos vão para as pastas que você escolher, já com nomes que o Jellyfin reconhece, e a biblioteca é atualizada sozinha.
- **Busca integrada**: a busca do Jellyfin passa a mostrar as fileiras *IPTV — Filmes* e *IPTV — Séries*, sem diferenciar acentos.
- **Fila de downloads** em *Painel → Downloads IPTV*, com retomada, novas tentativas, cancelamento e **pausa automática enquanto alguém assiste IPTV** (útil para contas de 1 conexão).

Nos apps de TV (Android TV, Roku, etc.), dá para assistir tudo. Os botões de download e a busca IPTV aparecem só no navegador e no app desktop.

## Requisitos

- **Jellyfin 10.10.x**
- Uma conta **Xtream Codes** (URL do servidor, usuário e senha)
- Plugin **[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)**, necessário para o botão de download e para a busca IPTV. Sem ele, o resto funciona normalmente.

## Instalação — passo a passo

### Opção 1: pelo catálogo do Jellyfin (recomendado)

Assim o Jellyfin também avisa quando sair uma versão nova.

1. Abra o Jellyfin como administrador e vá em **Painel → Plugins → Repositórios** (no 10.10: *Painel → Plugins*, aba **Repositórios**).
2. Clique em **+** e adicione os dois repositórios abaixo:

   | Nome | URL |
   |---|---|
   | JellyIPTV | `https://raw.githubusercontent.com/KsmKayk/jellyiptv/main/manifest.json` |
   | File Transformation | `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` |

3. Vá na aba **Catálogo**, encontre **File Transformation** e clique em **Instalar**.
4. No mesmo catálogo, encontre **JellyIPTV** (categoria *Live TV*) e clique em **Instalar**.
5. **Reinicie o Jellyfin** (*Painel → Reiniciar*, ou reinicie o container Docker).
6. Siga para a [Configuração](#configuração).

### Opção 2: manual, pelo arquivo da release

1. Instale o **File Transformation** pelo repositório da tabela acima, ou baixe-o na [página dele](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases).
2. Baixe o `jellyiptv_<versão>.zip` na [última release](https://github.com/KsmKayk/jellyiptv/releases/latest).
3. Na pasta de plugins do Jellyfin, crie uma pasta `JellyIPTV_<versão>` (ex.: `JellyIPTV_0.1.0.0`) e extraia o zip dentro dela. A pasta de plugins costuma ficar em:
   - Docker (linuxserver): `<config>/data/plugins/`
   - Linux (pacote): `/var/lib/jellyfin/plugins/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\`
4. No Linux/Docker, confira se a pasta pertence ao usuário do Jellyfin (ex.: `chown -R 1000:1000 JellyIPTV_0.1.0.0`).
5. **Reinicie o Jellyfin**.

## Configuração

1. Vá em **Painel → Plugins → JellyIPTV**.
2. Preencha a **URL do servidor** (ex.: `http://servidor.exemplo.com:8080`), o **usuário** e a **senha** da conta Xtream.
3. Clique em **Testar conexão**. Deve aparecer `✔ Conectado`, com o número de conexões e a data de validade da conta.
4. Escolha a **pasta de filmes** e a **pasta de séries** onde os downloads serão salvos. Use pastas que já façam parte das suas bibliotecas do Jellyfin, para os downloads aparecerem sozinhos.
5. Ajuste as opções (opcional):
   - **Downloads simultâneos**: deixe em 1 se a conta só permite uma conexão.
   - **Pausar downloads enquanto alguém assiste IPTV**: recomendado para contas de 1 conexão.
   - **Permitir download para não-admins**.
   - **Categorias ocultas**: esconde categorias de filmes, séries ou TV que você não quer ver.
6. Clique em **Salvar**. Isso também atualiza a lista de canais e o guia da TV ao vivo.

## Como usar

- **Filmes e séries**: na tela inicial, abra o canal **IPTV** (em *Canais*, ou na home).
- **TV ao vivo**: abra a seção **TV ao vivo** do menu lateral. Os canais e o guia levam de 1 a 2 minutos para aparecer depois de salvar a configuração.
- **Baixar**: no navegador ou no app desktop, abra um filme, série, temporada ou episódio do canal IPTV e use o botão flutuante **Baixar…** no canto da tela.
- **Acompanhar downloads**: **Painel → Downloads IPTV**.
- **Buscar**: use a busca normal do Jellyfin. Os resultados do IPTV aparecem no topo.

## Solução de problemas

| Problema | O que fazer |
|---|---|
| TV ao vivo vazia | Salve de novo a configuração do plugin ou rode **Painel → Tarefas agendadas → Atualizar guia**. Os canais aparecem em *TV ao vivo*, não dentro do canal IPTV. |
| Erro **403** ao testar a conexão ou ao assistir | Muitos provedores bloqueiam IPs de VPN e de datacenter. Se o servidor do Jellyfin usa VPN, libere o tráfego para o servidor IPTV (split tunneling) ou desligue a VPN. Confira também usuário e senha. |
| O botão de download e a busca IPTV não aparecem | Confira se o **File Transformation** está instalado e ativo, reinicie o Jellyfin e recarregue a página com **Ctrl+F5**. |
| O vídeo trava ao avançar | Normalmente é a conexão até o provedor, por exemplo uma VPN no meio do caminho. Teste a mesma URL em outro player. |
| Downloads parados | Se alguém está assistindo IPTV e a pausa automática está ligada, a fila volta sozinha quando a reprodução termina. |

## Limitações conhecidas

- Filmes são transcodificados ao assistir até o Jellyfin analisar o arquivo, porque o Xtream não informa o codec na listagem. Episódios usam remux.
- Itens de *Todos os filmes* / *Todas as séries* são cópias separadas dos itens das categorias: o progresso e o "assistido" não são compartilhados entre as duas.
- A primeira abertura de *Todos os filmes* cria um item por filme no banco do Jellyfin e pode demorar em catálogos grandes.
- Um título recém-adicionado ao provedor pode levar até 3 h para abrir pela busca, por causa do cache do canal no Jellyfin.
- A página *Downloads IPTV* fica no painel de administração.

## Desenvolvimento

Requer o [.NET SDK 8](https://dotnet.microsoft.com/download).

```bash
dotnet test                                  # roda os testes
dotnet publish src/JellyIptv -c Release      # gera JellyIptv.dll
build/package.sh "o que mudou"               # gera o zip da release e atualiza o manifest.json
```

Para publicar uma versão nova: suba o `<AssemblyVersion>` em `src/JellyIptv/JellyIptv.csproj`, rode `build/package.sh`, crie a release `v<versão>` com o zip de `artifacts/release/` e faça commit do `manifest.json` atualizado.

---

O JellyIPTV não fornece nenhum conteúdo: ele só se conecta a uma conta Xtream Codes que você já tem. Use apenas serviços e conteúdos que você tem direito de acessar.
