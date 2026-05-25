# EQ Flex Share — Cloudflare Worker

Zero-auth trigger sharing backend. Users POST a trigger package JSON, get back an 8-character share code. Anyone with the code can GET the package. Codes expire after 90 days.

## One-time setup

1. **Install Wrangler**
   ```
   npm install -g wrangler
   wrangler login
   ```

2. **Create the KV namespace**
   ```
   wrangler kv namespace create FLEX_SHARE
   ```
   Copy the `id` value from the output.

3. **Update `wrangler.toml`**
   Replace `REPLACE_WITH_KV_NAMESPACE_ID` with the id from step 2.

4. **Deploy**
   ```
   wrangler deploy
   ```
   The output shows your worker URL, e.g. `https://eq-flex-share.YOUR_ACCOUNT.workers.dev`.

5. **Update `TriggerShareService.WorkerBaseUrl`**
   In `src/EqFlex.App/Services/TriggerShareService.cs`, set the URL to your worker endpoint.
   Current: `https://eq-flex-share.eqflex.workers.dev`

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/share` | Upload JSON, returns `{"code": "ABC12345"}`. Rate limited to 20 req/IP/min. |
| `GET`  | `/share/{code}` | Fetch by 8-char code. Refreshes the 90-day TTL. 404 if expired or not found. |

## Share format

Share codes appear in EQ chat as: `{FLEX:share/ABC12345}`

EQ Flex detects this in live tailing and automatically offers to import.
