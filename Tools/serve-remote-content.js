const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");

const root = path.resolve(__dirname, "..", "Build", "RemoteContent");
const host = process.env.REMOTE_CONTENT_HOST || "0.0.0.0";
const port = Number(process.env.REMOTE_CONTENT_PORT || 18761);
const contentTypes = {
  ".bundle": "application/octet-stream",
  ".hash": "text/plain; charset=utf-8",
  ".json": "application/json; charset=utf-8",
};

http.createServer((request, response) => {
  if (request.method !== "GET" && request.method !== "HEAD") {
    response.writeHead(405, { Allow: "GET, HEAD" }).end();
    return;
  }

  let filePath;
  try {
    const urlPath = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
    filePath = path.resolve(root, "." + urlPath);
    const relative = path.relative(root, filePath);
    if (!relative || relative === ".." || relative.startsWith(".." + path.sep) || path.isAbsolute(relative)) {
      response.writeHead(404).end();
      return;
    }
  } catch {
    response.writeHead(400).end();
    return;
  }

  fs.stat(filePath, (error, stat) => {
    if (error || !stat.isFile()) {
      response.writeHead(404).end();
      return;
    }

    response.writeHead(200, {
      "Content-Type": contentTypes[path.extname(filePath).toLowerCase()] || "application/octet-stream",
      "Content-Length": stat.size,
    });
    if (request.method === "HEAD") {
      response.end();
      return;
    }
    fs.createReadStream(filePath).pipe(response);
  });
}).listen(port, host, () => {
  console.log(`Serving ${root} at http://${host}:${port}/`);
});
