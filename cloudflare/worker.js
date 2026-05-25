// EQ Flex Trigger Share — Cloudflare Worker
// Deploy: wrangler deploy
// KV namespace binding: FLEX_SHARE (create via wrangler kv namespace create FLEX_SHARE)

const ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ'; // 32 unambiguous chars (no 0/O, 1/I/l)
const CODE_LEN  = 8;
const TTL_SEC   = 90 * 24 * 60 * 60; // 90 days
const MAX_BODY  = 512 * 1024;         // 512 KB
const RATE_MAX  = 20;                 // max POSTs per IP per minute

function genCode() {
  const bytes = crypto.getRandomValues(new Uint8Array(CODE_LEN));
  return Array.from(bytes, b => ALPHABET[b % ALPHABET.length]).join('');
}

async function rateLimitOk(env, ip) {
  const key = `rate:${ip}:${Math.floor(Date.now() / 60000)}`;
  const n = parseInt(await env.FLEX_SHARE.get(key) ?? '0');
  if (n >= RATE_MAX) return false;
  await env.FLEX_SHARE.put(key, String(n + 1), { expirationTtl: 120 });
  return true;
}

const CORS_HEADERS = {
  'Access-Control-Allow-Origin':  '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS_HEADERS, 'Content-Type': 'application/json' },
  });
}

export default {
  async fetch(request, env) {
    const { method } = request;
    const { pathname } = new URL(request.url);

    if (method === 'OPTIONS') {
      return new Response(null, { headers: CORS_HEADERS });
    }

    // GET /share/{code}  — fetch trigger package by 8-char code
    const getMatch = pathname.match(/^\/share\/([A-Z0-9]{8})$/i);
    if (method === 'GET' && getMatch) {
      const code = getMatch[1].toUpperCase();
      const data = await env.FLEX_SHARE.get(`share:${code}`);
      if (!data) return json({ error: 'Share code not found or expired.' }, 404);
      // Refresh TTL on access so active codes stay alive
      await env.FLEX_SHARE.put(`share:${code}`, data, { expirationTtl: TTL_SEC });
      return new Response(data, { headers: { ...CORS_HEADERS, 'Content-Type': 'application/json' } });
    }

    // POST /share  — upload trigger package, get back a share code
    if (method === 'POST' && pathname === '/share') {
      const ip = request.headers.get('CF-Connecting-IP') ?? 'unknown';
      if (!await rateLimitOk(env, ip))
        return json({ error: 'Rate limit exceeded. Try again in a minute.' }, 429);

      let body;
      try { body = await request.text(); } catch {
        return json({ error: 'Failed to read request body.' }, 400);
      }
      if (!body || body.length === 0)
        return json({ error: 'Empty request body.' }, 400);
      if (body.length > MAX_BODY)
        return json({ error: `Payload too large (max ${MAX_BODY / 1024} KB).` }, 413);

      try { JSON.parse(body); } catch {
        return json({ error: 'Request body is not valid JSON.' }, 400);
      }

      // Generate a unique code (collision extremely unlikely with 32^8 = 1T possibilities)
      let code;
      for (let i = 0; i < 10; i++) {
        code = genCode();
        if (!await env.FLEX_SHARE.get(`share:${code}`)) break;
      }

      await env.FLEX_SHARE.put(`share:${code}`, body, { expirationTtl: TTL_SEC });
      return json({ code }, 201);
    }

    return new Response('Not found', { status: 404 });
  },
};
