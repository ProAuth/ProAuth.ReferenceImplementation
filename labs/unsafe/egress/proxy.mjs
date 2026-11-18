// Only the configured issuer's TLS endpoint is reachable. TLS remains end-to-end; no payload logging.
import http from 'node:http';
import net from 'node:net';
const issuer = new URL(process.env.PROAUTH_ISSUER);
if (issuer.protocol !== 'https:') throw Error('HTTPS issuer required');
const allowed = `${issuer.hostname}:${issuer.port || 443}`;
const server = http.createServer((req, res) => {
  res.writeHead(403);
  res.end();
});
server.on('connect', (req, socket, head) => {
  if (req.url !== allowed) {
    socket.end('HTTP/1.1 403 Forbidden\r\n\r\n');
    return;
  }
  const upstream = net.connect(Number(issuer.port || 443), issuer.hostname, () => {
    socket.write('HTTP/1.1 200 Connection Established\r\n\r\n');
    if (head.length) upstream.write(head);
    socket.pipe(upstream);
    upstream.pipe(socket);
  });
  upstream.setTimeout(10000, () => upstream.destroy());
  socket.setTimeout(10000, () => socket.destroy());
  upstream.on('error', () => socket.destroy());
  socket.on('error', () => upstream.destroy());
  socket.on('close', () => upstream.destroy());
});
server.listen(3128, '0.0.0.0');
