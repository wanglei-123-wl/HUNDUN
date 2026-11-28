"""Loopback-only artifact server used by native code-update acceptance tests."""
import argparse
import functools
import http.server
import json
import pathlib

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=pathlib.Path, required=True)
parser.add_argument("--port-record", type=pathlib.Path, required=True)
args = parser.parse_args()
handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(args.root.resolve()))
with http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler) as server:
    args.port_record.write_text(json.dumps({"baseUrl": "http://127.0.0.1:" + str(server.server_port) + "/"}), encoding="utf-8")
    print("LOOPBACK_ARTIFACT_SERVER_READY", flush=True)
    server.serve_forever()
