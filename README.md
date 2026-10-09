# Disgaea RPG — servidor local (offline)

Servidor local para a versão global do **DISGAEA RPG** na Steam, cujos servidores oficiais
(Boltrend) foram desligados em 12/05/2023. Com ele o jogo volta a abrir e é jogado offline,
com o progresso salvo no seu PC.

> *English: a local replacement server for the Steam/global release of DISGAEA RPG, whose
> official servers shut down on 2023-05-12. Requires your own copy of the game.*

**Status: em desenvolvimento.** Funciona: boot, login, tutorial completo e carregamento da
tela inicial. Fases, gacha, equipamentos e o resto do jogo ainda estão sendo implementados.

Este repositório **não contém nenhum arquivo do jogo** (executáveis, DLLs, assets ou master
data). Você precisa ter o jogo instalado pela Steam.

## Requisitos

- Windows, com o DISGAEA RPG instalado pela Steam
- Steam aberto (pode estar em modo offline) — o jogo fecha na abertura sem ele
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Como usar

1. **Redirecione o jogo para o servidor local** (uma vez só). No PowerShell, na pasta do repositório:
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\setup-game.ps1
   ```
   Se o jogo não estiver na pasta padrão da Steam, passe `-GameDir "D:\...\DISGAEA RPG"`.
   O `config_server.ini` original fica salvo como `config_server.ini.original`.

2. **Inicie o servidor** e deixe a janela aberta enquanto joga:
   ```powershell
   dotnet run
   ```
   Opções: `--port 8765`, `--game "<pasta do jogo>"`, `--data "<pasta dos saves>"`.

3. **Abra o jogo pela Steam.** Na tela de login digite qualquer nome de conta — ele só escolhe
   qual save usar (a senha é ignorada). O jogo lembra a última conta.

Para desfazer tudo: `powershell -ExecutionPolicy Bypass -File tools\restore-game.ps1`.

## Como funciona

- **Redirecionamento:** `StreamingAssets/settings/*.ini` são gzip + RC4. O `config_server.ini`
  passa a apontar para `http://127.0.0.1:8765/Server`, de onde o servidor entrega a lista de
  servidores e a configuração (`api`, `asset`, `master_bin`, …).
- **Login:** a config do SDK é servida com `"status": "Disable"`, o que faz o próprio jogo trocar o
  login web da Boltrend por uma janela simples de conta/senha (`/signin`).
- **Protocolo:** requisições AES-256-CBC + MessagePack (`/version_check`, `/signin`, `/rpc` em
  JSON-RPC). O cliente desserializa as respostas de forma muito estrita (chave desconhecida = erro,
  largura de inteiro importa, `List<T>` vira `{_items, _size}`), então o servidor carrega o
  `Assembly-CSharp.dll` **da sua instalação** por reflexão e monta cada resposta exatamente no
  tipo C# que o cliente espera. `methods.tsv` mapeia métodos RPC genéricos para esses tipos.
- **Master data:** a master data que vem na instalação é ~1 ano mais antiga que o código 3.2.10
  (o servidor oficial sempre enviava uma atualizada). Na inicialização o servidor detecta os campos
  que faltam em cada tabela e reescreve os arquivos extraídos em
  `%USERPROFILE%\AppData\LocalLow\Boltrend\DISGAEA RPG\Boltrend\XDMaster` (originais em `*.bak`).
- **Saves:** um JSON por conta em `bin/.../save/players/`.

## Estrutura

| Arquivo | Papel |
|---|---|
| `BootFiles.cs` | List.ini, config do servidor, config do SDK, CDN de assets local |
| `ApiRouter.cs` | criptografia, `/version_check`, `/signin`, despacho do `/rpc` |
| `Handlers.cs` | lógica dos métodos RPC (tutorial, dados do jogador, …) |
| `GameTypes.cs` / `SchemaWriter.cs` | tipos do cliente e serialização no formato exato |
| `MasterFix.cs` / `MasterData.cs` | atualização e leitura da master data |
| `Characters.cs`, `PlayerStore.cs` | personagens e saves |
| `tools/` | scripts para redirecionar/restaurar o jogo |

Métodos RPC ainda não implementados recebem uma resposta padrão bem-formada e aparecem no log
como `RPC sem implementação`.

## Aviso

Projeto de preservação, sem fins lucrativos e sem afiliação com Nippon Ichi Software,
Forward Works ou Boltrend. DISGAEA é marca de seus respectivos donos. Use apenas com uma cópia
legítima do jogo.
