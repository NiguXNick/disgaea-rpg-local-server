# Protocol

## Transport

- Body: **AES-256-CBC, PKCS7** over MessagePack. No compression.
- Key: `<read from the installed game>` (common key, `WebRequestAesCryptor`). After login the
  client switches to the `fuji_key` from `/signin`; this server returns the common key there.
- IV: random per request, base64 in the `X-Crypt-Iv` header. The response is encrypted with the
  same IV; echo the header back (the IV is shared static state in the client).

Request headers: `X-APP-VERSION`, `X-JVHPDR5CVHAHU5ZP` (OS type), `X_CHANNEL`,
`X_BUILD_VERSION`, `Content-Type: application/x-haut-hoiski`, `X-Crypt-Iv`, `X-SESSION`
(after login), `X_CHECK_SUBSCRIPTION` (RPC only).

Response headers: `X-Crypt-Iv`, `X-SERVER-UNIXTIME` (seconds; the client's clock),
`X-APP-STATUS: 10000` on RPC responses (without it the client treats the call as failed and
dereferences `api_error`). Paged lists use `X-IS-LAST-PAGE`: only the exact string `"False"`
fetches the next page.

## Endpoints

| Path | Kind | Body |
|---|---|---|
| `GET /version_check` | REST | → `{status: 0, store_url, need_update, app_review}` (hotfix reads `status`) |
| `POST /signin` | REST | `{uuid, password}` → `{session_id, fuji_key, is_new}` |
| `POST /rpc` | JSON-RPC | `{rpc: {jsonrpc, id, method, prms}}` where `prms` is a **JSON string** produced by Unity `JsonUtility` |

RPC response: `{jsonrpc: "2.0", id, result}`. Optional top-level keys: `error`, `api_error`
(`{code, status, message, behavior}`), `update_t_data`.

REST responses are read with a lenient `BoxingPacker` (dictionary); RPC results are read into
typed classes by the strict `ObjectPacker` (see serialization.md).

`prms` notes: `JsonUtility` sends null strings as `""` and null arrays as `[]`; numbers may be
large unsigned values. Read them with `Handlers.U/I/L/B`, never `(ulong)(node ?? 0)`.
